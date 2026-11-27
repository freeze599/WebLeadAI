using System.Collections.Concurrent;

namespace WebLeadAI.Security;

// A single-instance, in-memory throttle. Deploy behind a reverse proxy with
// appropriate IP-level rate limiting as an additional security layer.
public sealed class AdminLoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private sealed class Entry
    {
        public readonly object Sync = new();
        public DateTime WindowStart = DateTime.UtcNow;
        public int Failures;
    }

    public bool IsBlocked(string key)
    {
        var entry = _entries.GetOrAdd(key, _ => new Entry());
        lock (entry.Sync)
        {
            ResetExpired(entry);
            return entry.Failures >= MaxFailures;
        }
    }

    public void RecordFailure(string key)
    {
        var entry = _entries.GetOrAdd(key, _ => new Entry());
        lock (entry.Sync)
        {
            ResetExpired(entry);
            entry.Failures++;
        }
    }

    public void Reset(string key) => _entries.TryRemove(key, out _);

    private static void ResetExpired(Entry entry)
    {
        if (DateTime.UtcNow - entry.WindowStart < Window) return;
        entry.WindowStart = DateTime.UtcNow;
        entry.Failures = 0;
    }
}
