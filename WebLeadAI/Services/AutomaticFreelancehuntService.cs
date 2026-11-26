using WebLeadAI.Services;

namespace WebLeadAI.Services;

// Singleton: keeps manual and automatic discovery from saving the same leads concurrently.
public sealed class DiscoveryRunState
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    private readonly object _sync = new();
    private DateTime? _lastRunUtc;
    private int _lastAdded;
    private string? _lastMessage;
    public (DateTime? LastRunUtc, int Added, string? Message) Snapshot()
    {
        lock (_sync) return (_lastRunUtc, _lastAdded, _lastMessage);
    }
    public void Record(int added, string? message)
    {
        lock (_sync)
        {
            _lastRunUtc = DateTime.UtcNow;
            _lastAdded = added;
            _lastMessage = message;
        }
    }
}

// Runs in the ASP.NET app process. Stops when the application stops.
public sealed class AutomaticFreelancehuntService(
    IServiceScopeFactory scopes,
    DiscoveryRunState state,
    IConfiguration config,
    ILogger<AutomaticFreelancehuntService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("AutomaticDiscovery:Enabled", true))
        {
            logger.LogInformation("WebLeadAI automātiskā Freelancehunt meklēšana izslēgta konfigurācijā.");
            return;
        }

        // Never poll more often than the upstream 10-minute success cache interval.
        int minutes = Math.Clamp(config.GetValue("AutomaticDiscovery:IntervalMinutes", 10), 10, 1440);
        logger.LogInformation("WebLeadAI Freelancehunt automātiskā meklēšana ieslēgta: intervāls {Minutes} min.", minutes);
        try
        {
            // Avoid an immediate startup burst, especially during debugging.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                // Skip a tick if manual discovery is running; never overlap writes.
                if (await state.Gate.WaitAsync(0, stoppingToken))
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        var discovery = scope.ServiceProvider.GetRequiredService<LeadDiscoveryService>();
                        var report = await discovery.DiscoverFreelancehuntAsync();
                        var fh = report.FirstOrDefault(x => x.Source == "Freelancehunt");
                        int added = report.Sum(x => x.Added);
                        state.Record(added, fh?.Error);
                        logger.LogInformation("Freelancehunt automātiskā pārbaude pabeigta. Jauni: {Added}. Ziņojums: {Error}", added, fh?.Error ?? "nav");
                    }
                    catch (Exception ex)
                    {
                        // Avoid exposing tokens or raw response bodies in UI.
                        state.Record(0, "Automātiskās pārbaudes kļūda; skati lietotnes žurnālu.");
                        logger.LogError(ex, "Freelancehunt automātiskā pārbaude neizdevās.");
                    }
                    finally { state.Gate.Release(); }
                }
                await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
