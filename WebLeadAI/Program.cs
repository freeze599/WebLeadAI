
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebLeadAI.Security;

using Microsoft.EntityFrameworkCore;
using WebLeadAI.Data;
using WebLeadAI.Services;

var builder = WebApplication.CreateBuilder(args);
// Trust forwarded headers only from local Nginx.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Parse("127.0.0.1"));
    options.ForwardLimit = 1;
});

// Keep authentication cookies readable across service restarts and deployments.
// In production set DataProtection__KeysPath to a persistent, private directory.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    if (!Path.IsPathFullyQualified(dataProtectionKeysPath))
        throw new InvalidOperationException("DataProtection:KeysPath must be an absolute path.");

    builder.Services.AddDataProtection()
        .SetApplicationName("WebLeadAI")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}
else if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Production requires DataProtection__KeysPath pointing to a persistent, private directory.");
}

// MVC
builder.Services.AddControllersWithViews(options =>
{
    // Protect state-changing MVC requests automatically, including future actions.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name = "WebLeadAI.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            // APIs should receive 401 rather than an HTML sign-in page.
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build();
});
builder.Services.AddSingleton<AdminLoginThrottle>();


builder.Services.AddScoped<
    WebLeadAI.Services.LeadDiscoveryService>();


// SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString(
            "DefaultConnection")));


builder.Services.AddHttpClient<
    WebLeadAI.Services.RssLeadSourceService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(20);
        client.MaxResponseContentBufferSize = 5 * 1024 * 1024;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "WebLeadAI/1.0");
    }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddScoped<
    WebLeadAI.Services.LeadQualificationService>();

builder.Services.AddScoped<
    WebLeadAI.Services.LeadDescriptionCleanupService>();


// IUB atvērtie dati (bez API atslēgas).
builder.Services.AddHttpClient<IubLeadSourceService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("WebLeadAI/1.0");
    client.MaxResponseContentBufferSize = 20 * 1024 * 1024;
});

builder.Services.AddHttpClient<JobicyLeadSourceService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("WebLeadAI/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddHttpClient<FreelancehuntLeadSourceService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.MaxResponseContentBufferSize = 5 * 1024 * 1024;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("WebLeadAI/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
  .RedactLoggedHeaders(new[] { "Authorization" });

// Single-process gate and background Freelancehunt-only discovery.
builder.Services.AddSingleton<DiscoveryRunState>();
builder.Services.AddSingleton<NewLeadEmailNotifier>();
builder.Services.AddHostedService<AutomaticFreelancehuntService>();
builder.Services.AddHostedService<LeadEmailOutboxWorker>();

var app = builder.Build();

// One-time password-hash generation. Never saves or logs the plaintext password.
// This exits before any hosted services start or database is accessed.
if (args.Contains("--generate-admin-hash", StringComparer.OrdinalIgnoreCase))
{
    await AdminPasswordSetup.RunAsync();
    return;
}

// One-off notification pipeline test. No web server, discovery, or database writes.
// Run only on explicit request: dotnet run -- --test-email
if (args.Contains("--test-email", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        var notifier = app.Services.GetRequiredService<NewLeadEmailNotifier>();
        var sample = new[]
        {
            new NewLeadNotification(
                "TESTS — WebLeadAI v7 e-pasta paziņojumu pārbaude (nav jauns projekts)",
                "https://freelancehunt.com/project/development-landing-page-for/1666567.html",
                100,
                null)
        };
        var result = await notifier.SendAsync(sample, CancellationToken.None);
        Console.WriteLine(result ?? "Testa e-pasts netika nosūtīts (pārbaudi filtrus un konfigurāciju).");
    }
    catch (Exception ex)
    {
        // Do not display exception internals that might include SMTP connection details.
        Console.Error.WriteLine($"Testa nosūtīšana neizdevās: {ex.GetType().Name}. Pārbaudi lietotnes žurnālu un Gmail SMTP iestatījumus.");
    }
    return;
}
// Controlled SMTP failure and automatic-recovery test (no real SMTP credentials used on failure).
// Commands: --test-outbox-retry-prepare / --test-outbox-retry-fail / --test-outbox-retry-status
const string retryTestUrl = "https://freelancehunt.com/project/development-landing-page-for/1666567.html?webleadai_outbox_retry_test=v8";
if (args.Any(a => a is "--test-outbox-retry-prepare" or "--test-outbox-retry-fail" or "--test-outbox-retry-status"))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.LeadEmailOutbox.SingleOrDefaultAsync(x => x.SourceUrl == retryTestUrl);
        if (args.Contains("--test-outbox-retry-prepare"))
        {
            if (!builder.Configuration.GetValue("LeadNotifications:Enabled", false))
            {
                Console.WriteLine("Paziņojumi nav ieslēgti. Tests netika sākts.");
                return;
            }
            if (row is null)
            {
                if (await db.LeadEmailOutbox.AnyAsync(x => x.SentAtUtc == null))
                {
                    Console.WriteLine("Rindā jau ir īsti nenosūtīti ziņojumi. Testu neveidojam, vispirms apstrādā tos.");
                    return;
                }
                row = new WebLeadAI.Models.LeadEmailOutbox
                {
                    SourceUrl = retryTestUrl,
                    Title = "TESTS — WebLeadAI SMTP kļūmes atkopšana (NAV jauns projekts)",
                    Score = 100,
                    CreatedAtUtc = DateTime.UtcNow,
                    NextAttemptUtc = DateTime.UtcNow
                };
                db.LeadEmailOutbox.Add(row);
                await db.SaveChangesAsync();
                Console.WriteLine($"Izveidots tikai testa paziņojums ID {row.Id}. Izpildi --test-outbox-retry-fail.");
            }
            else Console.WriteLine($"Testa ieraksts jau eksistē (ID {row.Id}); dublikāts netika izveidots.");
        }
        else if (args.Contains("--test-outbox-retry-fail"))
        {
            if (row is null || row.Attempts != 0 || row.SentAtUtc is not null)
            {
                Console.WriteLine("Nav svaiga testa ieraksta. Vispirms izpildi --test-outbox-retry-prepare; vecos ierakstus nepārrakstām.");
                return;
            }
            // Intentionally unreachable localhost SMTP. No real Gmail secret is read or changed.
            var simulatedFailureConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LeadNotifications:Enabled"] = "true",
                    ["LeadNotifications:MinimumScore"] = "60",
                    ["LeadNotifications:To"] = "test@example.com",
                    ["LeadNotifications:From"] = "test@example.com",
                    ["LeadNotifications:Smtp:Host"] = "127.0.0.1",
                    ["LeadNotifications:Smtp:Port"] = "1",
                    ["LeadNotifications:Smtp:Username"] = "test",
                    ["LeadNotifications:Smtp:Password"] = "test-only-invalid"
                }).Build();
            var fakeSender = new NewLeadEmailNotifier(simulatedFailureConfig,
                app.Services.GetRequiredService<ILogger<NewLeadEmailNotifier>>());
            var testWorker = new LeadEmailOutboxWorker(
                app.Services.GetRequiredService<IServiceScopeFactory>(), fakeSender,
                builder.Configuration, app.Services.GetRequiredService<ILogger<LeadEmailOutboxWorker>>());
            await testWorker.ProcessTestItemOnceAsync(row.Id, fakeSender, CancellationToken.None);
            // Reload DB-tracked row after the worker's separate scope updated it.
            db.Entry(row).State = EntityState.Detached;
            row = await db.LeadEmailOutbox.SingleAsync(x => x.SourceUrl == retryTestUrl);
            Console.WriteLine(row.Attempts == 1 && row.SentAtUtc is null
                ? $"SIMULĒTĀ KĻŪME REĢISTRĒTA. ID {row.Id}; nākamais mēģinājums UTC: {row.NextAttemptUtc:yyyy-MM-dd HH:mm:ss}. Palaid parasto dotnet run."
                : "Negaidīts statuss. Pārbaudi --test-outbox-retry-status.");
        }
        if (args.Contains("--test-outbox-retry-status"))
        {
            if (row is null) Console.WriteLine("Testa ieraksta nav.");
            else Console.WriteLine($"ID {row.Id}; nosūtīts: {row.SentAtUtc.HasValue}; kļūmes: {row.Attempts}; kļūdas veids: {row.LastError ?? "nav"}; nākamais mēģinājums UTC: {row.NextAttemptUtc:yyyy-MM-dd HH:mm:ss}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Atkopšanas testa kļūda: {ex.GetType().Name}.");
        Environment.ExitCode = 1;
    }
    return;
}

// One-off, opt-in outbox integration test. Does not create a Lead or run discovery.
// Enqueue once: dotnet run -- --test-outbox-enqueue
// Inspect:      dotnet run -- --test-outbox-status
// Then run normally so the existing background worker delivers the queued message.
const string outboxTestUrl = "https://freelancehunt.com/project/development-landing-page-for/1666567.html?webleadai_outbox_test=v8";
if (args.Contains("--test-outbox-enqueue", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--test-outbox-status", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var testItem = await db.LeadEmailOutbox.SingleOrDefaultAsync(x => x.SourceUrl == outboxTestUrl);
        if (args.Contains("--test-outbox-enqueue", StringComparer.OrdinalIgnoreCase))
        {
            if (!builder.Configuration.GetValue("LeadNotifications:Enabled", false))
            {
                Console.WriteLine("Paziņojumi nav ieslēgti. Nekas netika pievienots.");
                return;
            }
            if (testItem is null)
            {
                testItem = new WebLeadAI.Models.LeadEmailOutbox
                {
                    SourceUrl = outboxTestUrl,
                    Title = "TESTS — WebLeadAI v8 paziņojumu rindas pārbaude (NAV klienta projekts)",
                    Score = 100,
                    CreatedAtUtc = DateTime.UtcNow,
                    NextAttemptUtc = DateTime.UtcNow
                };
                db.LeadEmailOutbox.Add(testItem);
                await db.SaveChangesAsync();
                Console.WriteLine($"Testa ziņojums pievienots rindā (ID {testItem.Id}). Tagad palaid dotnet run.");
            }
            else
            {
                Console.WriteLine($"Testa ziņojums jau ir rindā (ID {testItem.Id}); jauns netika veidots.");
            }
        }
        if (testItem is null)
        {
            Console.WriteLine("Testa ziņojums rindā vēl nav izveidots.");
        }
        else
        {
            string status = testItem.SentAtUtc.HasValue ? "NOSŪTĪTS" :
                testItem.NextAttemptUtc > DateTime.UtcNow ? "GAIDA ATKĀRTOTU MĒĢINĀJUMU" : "GAIDA NOSŪTĪŠANU";
            Console.WriteLine($"ID: {testItem.Id}; statuss: {status}; mēģinājumi ar kļūdu: {testItem.Attempts}.");
            if (testItem.SentAtUtc.HasValue)
                Console.WriteLine($"Nosūtīts (UTC): {testItem.SentAtUtc.Value:yyyy-MM-dd HH:mm:ss}");
            else if (testItem.Attempts > 0)
                Console.WriteLine($"Nākamais mēģinājums (UTC): {testItem.NextAttemptUtc:yyyy-MM-dd HH:mm:ss}; kļūdas veids: {testItem.LastError}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Rindas testa kļūda: {ex.GetType().Name}. Pārbaudi, vai v8 migrācija ir izpildīta.");
        Environment.ExitCode = 1;
    }
    return;
}

// Read-only inspection of the latest 15 outbox messages. No server starts, no DB writes.
// Usage: dotnet run -- --outbox-list
if (args.Contains("--outbox-list", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messages = await db.LeadEmailOutbox
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Take(15)
            .ToListAsync();

        Console.WriteLine($"LeadEmailOutbox: pēdējie {messages.Count} ieraksti (tikai lasīšana)");
        if (messages.Count == 0)
            Console.WriteLine("Paziņojumu rinda ir tukša.");

        foreach (var item in messages)
        {
            var isTest = item.Title.StartsWith("TESTS", StringComparison.OrdinalIgnoreCase) ||
                         item.SourceUrl.Contains("webleadai_outbox_test=", StringComparison.OrdinalIgnoreCase) ||
                         item.SourceUrl.Contains("webleadai_outbox_retry_test=", StringComparison.OrdinalIgnoreCase);
            var status = item.SentAtUtc.HasValue ? "NOSŪTĪTS" :
                item.NextAttemptUtc > DateTime.UtcNow ? "GAIDA ATKĀRTOŠANU" : "GAIDA NOSŪTĪŠANU";
            Console.WriteLine($"ID {item.Id} | {(isTest ? "TESTS" : "PROJEKTS")} | {status} | punkti {item.Score} | kļūmes {item.Attempts}");
            Console.WriteLine($"  Nosaukums: {item.Title}");
            Console.WriteLine($"  Avots: {item.SourceUrl}");
            Console.WriteLine($"  Izveidots UTC: {item.CreatedAtUtc:yyyy-MM-dd HH:mm:ss}; nosūtīts UTC: {(item.SentAtUtc.HasValue ? item.SentAtUtc.Value.ToString("yyyy-MM-dd HH:mm:ss") : "nav")}");
            if (!item.SentAtUtc.HasValue)
                Console.WriteLine($"  Nākamais mēģinājums UTC: {item.NextAttemptUtc:yyyy-MM-dd HH:mm:ss}; kļūda: {item.LastError ?? "nav"}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Paziņojumu rindas nolasīšana neizdevās: {ex.GetType().Name}.");
        Environment.ExitCode = 1;
    }
    return;
}

// Vienreizēja esošo IUB ierakstu strukturēšana pēc migrācijas.
if (args.Contains("--backfill-procurements"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var leads = await db.Leads.Where(l => l.SourceType == "Procurement").ToListAsync();
    foreach (var lead in leads)
        ProcurementFields.FillMissing(lead, lead.Description);
    await db.SaveChangesAsync();
    Console.WriteLine($"IUB metadatu pārnese pabeigta. Pārskatīti {leads.Count} iepirkumi.");
    return;
}

if (args.Contains("--cleanup-descriptions-preview") ||
    args.Contains("--cleanup-descriptions-apply"))
{
    bool apply = args.Contains("--cleanup-descriptions-apply");

    using var scope = app.Services.CreateScope();

    var cleanup = scope.ServiceProvider
        .GetRequiredService<
            WebLeadAI.Services.LeadDescriptionCleanupService>();

    var result = await cleanup.CleanAsync(apply);

    Console.WriteLine("WebLeadAI aprakstu tīrīšana");
    Console.WriteLine($"Kopā: {result.Total}");
    Console.WriteLine($"Atpazīti: {result.Matched}");
    Console.WriteLine($"Iztīrīti: {result.Cleaned}");
    Console.WriteLine($"Izlaisti (tukšs oriģināls): {result.Skipped}");
    Console.WriteLine(
        apply
            ? "Izmaiņas saglabātas."
            : "Priekšskatījums — datubāze nav mainīta.");

    return;
}



// Process trusted Nginx headers before HTTPS and authentication.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();


