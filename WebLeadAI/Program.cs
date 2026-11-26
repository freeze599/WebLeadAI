
using Microsoft.EntityFrameworkCore;
using WebLeadAI.Data;
using WebLeadAI.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();


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



if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();


