
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "WebLeadAI/1.0");
    });

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

var app = builder.Build();
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


