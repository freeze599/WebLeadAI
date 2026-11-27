using Microsoft.EntityFrameworkCore;
using WebLeadAI.Data;

namespace WebLeadAI.Services;

// Delivers persisted notifications independently from the discovery interval.
// This is an at-least-once queue: a process crash after SMTP accepts a message but
// before SentAtUtc is persisted can result in one duplicate email.
public sealed class LeadEmailOutboxWorker(
    IServiceScopeFactory scopes,
    NewLeadEmailNotifier notifier,
    IConfiguration config,
    ILogger<LeadEmailOutboxWorker> logger) : BackgroundService
{
    // Explicit, one-item diagnostic entry point. Never processes unrelated queued mail.
    // Used only by the --test-outbox-retry-fail CLI branch in Program.cs.
    public async Task ProcessTestItemOnceAsync(int id, NewLeadEmailNotifier testNotifier, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.LeadEmailOutbox.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null || item.SentAtUtc is not null)
            throw new InvalidOperationException("Testa ieraksts neeksistē vai jau nosūtīts.");
        if (item.Attempts != 0 || item.NextAttemptUtc > DateTime.UtcNow)
            throw new InvalidOperationException("Testam vajadzīgs svaigs, vēl nemēģināts ieraksts.");
        await ProcessItemAsync(db, item, testNotifier, cancellationToken);
    }

    private async Task ProcessItemAsync(
        AppDbContext db,
        WebLeadAI.Models.LeadEmailOutbox item,
        NewLeadEmailNotifier emailSender,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await emailSender.SendAsync(
                new[] { new NewLeadNotification(item.Title, item.SourceUrl, item.Score, item.PublishedAt) },
                cancellationToken);
            if (result is null || !result.StartsWith("E-pastā nosūtīti", StringComparison.Ordinal))
                throw new InvalidOperationException("E-pasta serviss neatgrieza nosūtīšanas apstiprinājumu.");
            item.SentAtUtc = DateTime.UtcNow;
            item.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Paziņojums {Id} nosūtīts (mēģinājums {Attempt}).", item.Id, item.Attempts + 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            item.Attempts++;
            double minutes = Math.Min(60, Math.Pow(2, Math.Min(item.Attempts - 1, 6)));
            item.NextAttemptUtc = DateTime.UtcNow.AddMinutes(minutes);
            item.LastError = ex.GetType().Name;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Paziņojums {Id} neizdevās ({Error}); atkārtos pēc {Minutes} min.",
                item.Id, ex.GetType().Name, minutes);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("LeadNotifications:Enabled", false))
        {
            logger.LogInformation("WebLeadAI e-pasta paziņojumu rinda izslēgta.");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTime.UtcNow;
                    // Single application instance only. Parallel deployed instances
                    // need a database-backed lease to avoid concurrent sends.
                    var due = await db.LeadEmailOutbox
                        .Where(x => x.SentAtUtc == null && x.NextAttemptUtc <= now)
                        .OrderBy(x => x.Id).Take(20).ToListAsync(stoppingToken);
                    foreach (var item in due)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        await ProcessItemAsync(db, item, notifier, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError("E-pasta rindas apstrāde neizdevās: {Error}.", ex.GetType().Name);
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
