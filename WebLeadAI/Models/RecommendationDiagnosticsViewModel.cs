using WebLeadAI.Services;

namespace WebLeadAI.Models;

public sealed record RecommendationDiagnosticItem(Lead Lead, ActionabilityService.Result Assessment);
public sealed record RecommendationReasonCount(string Code, string Label, int Count);

public sealed class RecommendationDiagnosticsViewModel
{
    public DateTime EvaluatedAtUtc { get; init; }
    public int TotalCount { get; init; }
    public int RecommendedCount { get; init; }
    public int ReviewCount { get; init; }
    public int SkippedCount { get; init; }
    public string Reason { get; init; } = "";
    public IReadOnlyList<RecommendationReasonCount> Reasons { get; init; } = [];
    public IReadOnlyList<RecommendationDiagnosticItem> Items { get; init; } = [];

    // Kopsavilkums vienmēr aptver visu datubāzi; filtrs maina tikai tabulas rindas.
    public static RecommendationDiagnosticsViewModel Create(
        IEnumerable<Lead> leads, DateTime utcNow, string? reason = null)
    {
        var assessed = leads.Select(l => new RecommendationDiagnosticItem(
            l, ActionabilityService.Evaluate(l, utcNow))).ToList();
        var reasons = assessed.Where(x => x.Assessment.PrimaryExclusion != null)
            .GroupBy(x => x.Assessment.PrimaryExclusion!.Code)
            .Select(g => new RecommendationReasonCount(g.Key,
                g.First().Assessment.PrimaryExclusion!.Label, g.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Code).ToList();

        var selected = reason == "recommended" || reasons.Any(x => x.Code == reason)
            ? reason! : "";
        var visible = assessed.Where(x => selected == "" ||
            (selected == "recommended" ? x.Assessment.Category == "recommended" :
                x.Assessment.PrimaryExclusion?.Code == selected));

        return new RecommendationDiagnosticsViewModel
        {
            EvaluatedAtUtc = utcNow,
            TotalCount = assessed.Count,
            RecommendedCount = assessed.Count(x => x.Assessment.Category == "recommended"),
            ReviewCount = assessed.Count(x => x.Assessment.Category == "review"),
            SkippedCount = assessed.Count(x => x.Assessment.Category == "skip"),
            Reason = selected,
            Reasons = reasons,
            Items = visible.OrderBy(x => x.Assessment.Rank)
                .ThenByDescending(x => x.Lead.AILeadScore).ThenBy(x => x.Lead.Id).ToList()
        };
    }
}
