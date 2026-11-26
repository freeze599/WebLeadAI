namespace WebLeadAI.Models;

// Durable queue. Inserted together with a newly discovered lead in the same EF SaveChanges.
public sealed class LeadEmailOutbox
{
    public int Id { get; set; }
    public string SourceUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public int Score { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
