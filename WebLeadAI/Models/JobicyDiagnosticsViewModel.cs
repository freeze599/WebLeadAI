using WebLeadAI.Services;

namespace WebLeadAI.Models;

public sealed record JobicyExclusion(string Code, string Label);
public sealed record JobicyItemDiagnostics(string JobTypes, string Geography, IReadOnlyList<JobicyExclusion> Reasons);
public sealed record JobicyReasonSummary(string Code, string Label, int PrimaryCount, int AnyCount);
public sealed record JobicyDiagnosticRow(string Title, string Url, DateTime? PublishedAt, JobicyItemDiagnostics Assessment);

public sealed class JobicyDiagnosticsViewModel
{
    public bool HasSnapshot { get; init; }
    public DateTime? FetchedAtUtc { get; init; }
    public string? Error { get; init; }
    public string? Notice { get; init; }
    public int Read { get; init; }
    public int Eligible { get; init; }
    public int Rejected => Read - Eligible;
    public string Reason { get; init; } = "";
    public IReadOnlyList<JobicyReasonSummary> Reasons { get; init; } = [];
    public IReadOnlyList<JobicyDiagnosticRow> Rows { get; init; } = [];

    public static JobicyDiagnosticsViewModel Create(RssFeedResult? feed, string? reason = null)
    {
        if (feed == null) return new();
        var rows = feed.Items.Where(x => x.JobicyDiagnostics != null).Select(x => new JobicyDiagnosticRow(
            x.Title, x.Url, x.PublishedAt, x.JobicyDiagnostics!)).ToList();
        var reasons = rows.SelectMany(x => x.Assessment.Reasons).DistinctBy(x => x.Code)
            .Select(r => new JobicyReasonSummary(r.Code, r.Label,
                rows.Count(x => x.Assessment.Reasons.FirstOrDefault()?.Code == r.Code),
                rows.Count(x => x.Assessment.Reasons.Any(y => y.Code == r.Code))))
            .OrderByDescending(x => x.PrimaryCount).ThenBy(x => x.Code).ToList();
        var selected = reason is "eligible" or "rejected" || reasons.Any(x => x.Code == reason) ? reason! : "";
        return new()
        {
            HasSnapshot = true, FetchedAtUtc = feed.FetchedAtUtc, Error = feed.Error, Notice = feed.Notice,
            Read = rows.Count, Eligible = rows.Count(x => x.Assessment.Reasons.Count == 0), Reasons = reasons,
            Reason = selected,
            Rows = rows.Where(x => selected == "" || (selected == "eligible" ? x.Assessment.Reasons.Count == 0 :
                selected == "rejected" ? x.Assessment.Reasons.Count > 0 : x.Assessment.Reasons.Any(y => y.Code == selected))).ToList()
        };
    }
}
