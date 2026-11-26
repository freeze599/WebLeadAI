using System.Globalization;
using WebLeadAI.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebLeadAI.Services;

public sealed class JobicyLeadSourceService(HttpClient http, ILogger<JobicyLeadSourceService> logger)
{
    public const string ContractType = "ContractOpportunity";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static volatile RssFeedResult? cached;
    // Tikai atmiņā: diagnostikas GET neveic meklēšanu un neraksta datubāzē.
    public static JobicyDiagnosticsViewModel GetDiagnostics(string? reason = null) => JobicyDiagnosticsViewModel.Create(cached, reason);
    private static DateTime nextFetch;
    public async Task<RssFeedResult> GetFeedResultAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (cached != null && DateTime.UtcNow < nextFetch) return cached;
            var result = new RssFeedResult { Source = "Jobicy" };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? cursor = null;
            nextFetch = DateTime.UtcNow.AddMinutes(30);
            try
            {
                for (var page = 0; page < 5; page++)
                {
                    if (page > 0) await Task.Delay(1100, deadline.Token);
                    var url = "https://jobicy.com/api/jobs?count=100&industry=engineering" +
                        (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
                    using var response = await http.GetAsync(url, deadline.Token);
                    if ((int)response.StatusCode == 429)
                    {
                        var retry = response.Headers.RetryAfter;
                        var until = retry?.Date?.UtcDateTime ?? DateTime.UtcNow.Add(retry?.Delta ?? TimeSpan.FromMinutes(30));
                        if (until > nextFetch) nextFetch = until;
                    }
                    response.EnsureSuccessStatusCode();
                    var text = await response.Content.ReadAsStringAsync(deadline.Token);
                    var parsed = ParsePage(text, DateTime.UtcNow);
                    foreach (var item in parsed.Items)
                        if (seen.Add(item.Url)) result.Items.Add(item);
                    cursor = parsed.Cursor;
                    if (string.IsNullOrEmpty(cursor)) break;
                    if (page == 4) result.Notice = "Pārbaudītas pirmās 5 lapas (līdz 500 ierakstiem); avotā ir vēl rezultāti.";
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or RegexMatchTimeoutException)
            {
                result.Error = "Jobicy imports nepilnīgs vai nepieejams: " + ex.Message;
                logger.LogWarning(ex, "Jobicy imports nav pilnībā pabeigts");
            }
            result.FetchedAtUtc = DateTime.UtcNow;
            cached = result;
            return result;
        }
        finally { Gate.Release(); }
    }

    public static (List<RssLeadItem> Items, string? Cursor) ParsePage(string json, DateTime utcNow)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False) ||
            !root.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Jobicy atbilde neatbilst API formātam.");
        var items = new List<RssLeadItem>();
        foreach (var job in jobs.EnumerateArray())
        {
            string S(string key) => job.ValueKind == JsonValueKind.Object && job.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var title = LeadContentText.ToPlainText(S("jobTitle"));
            var url = S("url");
            if (string.IsNullOrWhiteSpace(title) || url.Length > 500 || !Uri.TryCreate(url, UriKind.Absolute, out var u) ||
                u.Scheme != "https" || u.Host != "jobicy.com" || u.Port != 443 || u.UserInfo != "" || !u.AbsolutePath.StartsWith("/jobs/")) continue;
            var description = LeadContentText.ToPlainText(S("jobDescription"));
            var geo = S("jobGeo");
            var types = job.TryGetProperty("jobType", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").ToArray() : [];
            var knownDate = DateTimeOffset.TryParse(S("pubDate"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var date);
            var reasons = new List<JobicyExclusion>();
            if (types.Any(x => x.Equals("Full-Time", StringComparison.OrdinalIgnoreCase)))
                reasons.Add(new("full_time", "Pilnas slodzes darbs"));
            else if (!types.Any(x => x.Equals("Contract", StringComparison.OrdinalIgnoreCase) || x.Equals("Freelance", StringComparison.OrdinalIgnoreCase) || x.Equals("Part-Time", StringComparison.OrdinalIgnoreCase)))
                reasons.Add(new("job_type", "Nav norādīts līgumdarbs vai nepilna slodze"));
            if (!IsWebRole(title, description))
                reasons.Add(new("role", "Nav atpazīta tīmekļa izstrādes loma"));
            if (!AllowsLatvia(geo))
                reasons.Add(new(string.IsNullOrWhiteSpace(geo) ? "geo_unknown" : "geo",
                    string.IsNullOrWhiteSpace(geo) ? "Darba reģions nav norādīts" : "Darba reģiona norāde neatbilst atlasei"));
            if (!knownDate) reasons.Add(new("date_unknown", "Publicēšanas datums nav atpazīts"));
            else if (date.UtcDateTime > utcNow.AddMinutes(5)) reasons.Add(new("future", "Publicēšanas datums ir nākotnē"));
            else if (date.UtcDateTime < utcNow.AddDays(-30)) reasons.Add(new("old", "Vecāks par 30 dienām"));
            if (description.Length is < 30 or > 60000)
                reasons.Add(new("description", "Apraksts ir pārāk īss vai neparasti garš"));
            var candidate = reasons.Count == 0;
            items.Add(new RssLeadItem
            {
                Title = title, Url = url, Source = "Jobicy", SourceType = ContractType,
                IsCandidate = candidate,
                JobicyDiagnostics = new(string.Join(", ", types), LeadContentText.ToPlainText(geo), reasons.AsReadOnly()),
                PublishedAt = knownDate ? date.UtcDateTime : null,
                Description = description + "\n\nAvots: Jobicy\nDarba devējs: " + LeadContentText.ToPlainText(S("companyName")) +
                    "\nDarba veids: " + string.Join(", ", types) + "\nAtļautā darba vieta (avota norāde): " + LeadContentText.ToPlainText(geo) +
                    "\nŠis ir līgumdarba vai nepilnas slodzes piedāvājums, nevis atsevišķs klienta pasūtījums. Precizē darba stundas, pieredzi un pieņemšanu no Latvijas."
            });
        }
        var cursor = root.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        if (root.TryGetProperty("hasMore", out var more) && more.ValueKind == JsonValueKind.True && string.IsNullOrEmpty(cursor))
            throw new InvalidOperationException("Jobicy norāda nākamo lapu bez turpinājuma marķiera.");
        return (items, cursor);
    }
    public static bool AllowsLatvia(string geo) => geo.Split(',').Select(x => x.Trim().ToLowerInvariant())
        .Any(x => x is "anywhere" or "worldwide" or "global" or "europe" or "european union" or "eu" or "emea" or "latvia");
    public static bool IsWebRole(string title, string description) =>
        Regex.IsMatch(title, @"\b(web|full[- ]?stack|front[- ]?end|back[- ]?end|asp\.net|\.net|blazor)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
        || (Regex.IsMatch(title, @"\b(developer|engineer)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
            && new[] { "asp.net", "blazor", ".net core", "c#" }.Any(x => description.Contains(x, StringComparison.OrdinalIgnoreCase)));

    public static LeadQualificationResult Evaluate(string title, string description)
    {
        bool dotnet = new[] { "asp.net", "blazor", ".net", "c#" }.Any(x => (title + " " + description).Contains(x, StringComparison.OrdinalIgnoreCase));
        int score = dotnet ? 65 : 45;
        return new() { IsQualified = true, Score = score, SkillMatch = score, OrderConfidence = 0,
            Reason = "Līgumdarbs / nepilna slodze; jāprecizē nosacījumi, nevis klienta pasūtījuma vērtējums.",
            OrderReason = "Nav piemērojams: šis ir darba piedāvājums.",
            SkillReason = dotnet ? "Minēta .NET/C# tehnoloģija; pieredzes prasības jāpārbauda." : "Tīmekļa izstrādes loma; tehnoloģiju un pieredzes atbilstība jāpārbauda." };
    }
}
