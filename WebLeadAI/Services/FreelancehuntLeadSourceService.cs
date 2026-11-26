using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebLeadAI.Services;

// Read-only API integration: no bids, messages or account changes.
public sealed class FreelancehuntLeadSourceService(HttpClient client, IConfiguration configuration)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static RssFeedResult? cached;
    private static string? cachedKey;
    private static DateTime cacheUntil;
    private const int MaxPages = 5, MaxItems = 250, MaxDetails = 35;
    private const int HighCompetitionBidCount = 100; // Warning only; not a claim that bids are closed.
    // Freelancehunt skill IDs: Website Development (96), Web Programming (99).
    // Query each skill separately to avoid exhausting detail requests on unrelated jobs.
    private static readonly int[] TargetSkills = [96, 99];
    private const string Endpoint = "https://api.freelancehunt.com/v2/projects";

    public async Task<RssFeedResult> GetFeedResultAsync()
    {
        string token = configuration["Freelancehunt:ApiToken"]?.Trim() ?? "";
        if (token.Length == 0)
            return new() { Source = "Freelancehunt", Error = "Nav iestatīta Freelancehunt:ApiToken atslēga. Pārbaudi Manage User Secrets un Development palaišanas vidi." };
        if (token.Length > 4096 || token.Any(char.IsWhiteSpace))
            return new() { Source = "Freelancehunt", Error = "API atslēgas formāts nav derīgs. Ievieto tikai atslēgu, bez Bearer prefiksa." };
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        await Gate.WaitAsync();
        try
        {
            if (cached != null && cachedKey == key && DateTime.UtcNow < cacheUntil) return cached;
            var result = new RssFeedResult { Source = "Freelancehunt" };
            int omitted = 0, detailCount = 0, plus = 0, incomplete = 0, unknownPlus = 0, received = 0, checkedPages = 0;
            int highCompetition = 0, bidsUnknown = 0, recentCandidates = 0;
            var candidateReasons = new Dictionary<string, int>();
            var skipped = new Dictionary<string, int>();
            void Skip(string reason) { omitted++; skipped[reason] = skipped.GetValueOrDefault(reason) + 1; }
            int formatFailures = 0;
            bool limited = false;
            TimeSpan retry = TimeSpan.FromMinutes(2);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var seen = new HashSet<long>();
            try
            {
                foreach (int skill in TargetSkills)
                {
                    for (int page = 1; page <= MaxPages; page++)
                    {
                        // Fixed-origin requests only. Never send the API credential to a feed-provided URL.
                        if (checkedPages > 0) await Task.Delay(700, deadline.Token);
                        checkedPages++;
                        using var document = await ReadAsync(Endpoint + "?filter[skill_id]=" + skill + "&filter[status_id]=11&page[number]=" + page, token, deadline.Token);
                        var root = document.RootElement;
                        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                            throw new ImportException("API atbilde nesatur projektu sarakstu.");
                        if (data.GetArrayLength() == 0) break;
                        foreach (var summary in data.EnumerateArray())
                        {
                            if (received >= MaxItems) { limited = true; break; }
                            received++;
                            long id = Id(summary);
                            if (id <= 0) { Skip("nederīgs ID"); formatFailures++; continue; }
                            if (!seen.Add(id)) { Skip("atkārtots ID API atbildē"); continue; }
                            // If the summary explicitly proves that bidding is unavailable, do not
                            // spend one of the limited detail requests on it. Unknown fields are
                            // NOT interpreted as rejection (the API may omit them in summaries).
                            if (KnownUnavailable(summary, DateTime.UtcNow, out string unavailableReason))
                            {
                                Skip(unavailableReason);
                                continue;
                            }
                            JsonElement project = summary;
                            if (!Complete(summary))
                            {
                                if (detailCount >= MaxDetails) { limited = true; Skip("sasniegts pilno aprakstu pieprasījumu limits"); continue; }
                                await Task.Delay(700, deadline.Token);
                                detailCount++;
                                using var details = await ReadAsync(Endpoint + "/" + id, token, deadline.Token);
                                if (details.RootElement.ValueKind != JsonValueKind.Object || !details.RootElement.TryGetProperty("data", out var detail) || Id(detail) != id)
                                { Skip("detaļu atbildē trūkst atbilstoša projekta"); formatFailures++; continue; }
                                project = detail.Clone();
                            }
                            var item = ParseProject(project, DateTime.UtcNow, out var parseFailure);
                            if (item == null) { Skip(parseFailure!); formatFailures++; continue; }
                            var attributes = project.GetProperty("attributes");
                            if (Flag(attributes, "is_only_for_plus") == null) unknownPlus++;
                            if (item.IsCandidate)
                            {
                                var publishedAt = Date(attributes, "published_at");
                                if (publishedAt.HasValue && publishedAt >= DateTime.UtcNow.AddDays(-2)) recentCandidates++;
                                if (Count(attributes, "bid_count") is int bids)
                                {
                                    if (bids >= HighCompetitionBidCount) highCompetition++;
                                }
                                else bidsUnknown++;
                            }
                            if (!item.IsCandidate)
                            {
                                string reason = CandidateRejectionReason(project, DateTime.UtcNow);
                                candidateReasons[reason] = candidateReasons.GetValueOrDefault(reason) + 1;
                                if (reason == "tikai Plus") plus++;
                                else incomplete++;
                            }
                            result.Items.Add(item);
                        }
                        bool hasNext = root.TryGetProperty("links", out var links) && !string.IsNullOrWhiteSpace(Text(links, "next"));
                        // Never follow API-supplied URLs with the credential. Construct fixed-origin pages.
                        if (hasNext && received >= MaxItems) limited = true;
                        if (!hasNext || received >= MaxItems) break;
                        if (page == MaxPages) limited = true;
                    }
                    if (received >= MaxItems) { limited = true; break; }
                }
            }
            catch (ImportException ex) { result.Error = ex.Message; retry = ex.RetryAfter ?? retry; }
            catch (OperationCanceledException) { result.Error = "Freelancehunt atbildes gaidīšanas laiks pārsniegts. Saglabāti tikai līdz šim nolasītie kandidāti."; }
            catch (HttpRequestException) { result.Error = "Freelancehunt savienojums neizdevās. Mēģini vēlāk."; }
            catch (JsonException) { result.Error = "Freelancehunt atgrieza nederīgu JSON. Imports nepabeigts."; }
            bool requestFailed = result.Error != null;
            if (result.Error == null && formatFailures > 0)
                result.Error = "Imports nepilnīgs: daļu API ierakstu nevarēja nolasīt. Skati izlaišanas iemeslus.";
            else if (result.Error == null && limited)
                result.Error = "Daļēja pārbaude: sasniegts importa limits; nav pārbaudīti visi API piedāvājumi.";
            result.FetchedAtUtc = DateTime.UtcNow;
            result.Notice = $"Filtrs: mājaslapu izstrāde (96), web programmēšana (99), API statuss 11 (atvērts pieteikumiem); API lapas: {checkedPages}. Saņemti API ieraksti: {received}; nolasīti: {result.Items.Count}; detaļu pieprasījumi: {detailCount}. API: līdz {MaxPages} lapām katrai prasmei / {MaxItems} ierakstiem kopā; pilnie apraksti līdz {MaxDetails} papildu pieprasījumos. No API kandidātiem publicēti pēdējās 48 stundās: {recentCandidates}; ar vismaz {HighCompetitionBidCount} piedāvājumiem: {highCompetition}; bez piedāvājumu skaita: {bidsUnknown}. Liels piedāvājumu skaits ir brīdinājums, nevis pierādījums slēgtai pieteikšanai. Pieteikšanās iespēja vienmēr jāpārbauda oriģinālajā lapā. Plus izslēgti: {plus}; Plus statuss nav norādīts: {unknownPlus} (pieeja jāpārbauda); citi nepieejami/nepilnīgi: {incomplete}; nenolasāmi/izlaisti: {omitted}. " +
                (candidateReasons.Count > 0 ? "Neatbilstošu projektu iemesli: " + string.Join("; ", candidateReasons.Select(x => $"{x.Key}: {x.Value}")) + ". " : "") +
                (skipped.Count > 0 ? "Izlaisti: " + string.Join("; ", skipped.Select(x => $"{x.Key}: {x.Value}")) + ". " : "") +
                "Profila pieteikšanās piekļuve vēl jāpārbauda — atradumus skaties sadaļā ‘Piekļuve jāpārbauda’, avots freelancehunt.com. Pieteikumi netiek sūtīti.";
            cached = result; cachedKey = key;
            // Shorter successful cache favors new projects while still avoiding repeated API calls.
            cacheUntil = DateTime.UtcNow.Add(!requestFailed && formatFailures == 0 ? TimeSpan.FromMinutes(10) : retry);
            return result;
        }
        finally { Gate.Release(); }
    }

    private async Task<JsonDocument> ReadAsync(string url, string token, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.AcceptLanguage.ParseAdd("en");
        using var response = await client.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delay = response.Headers.RetryAfter?.Delta ??
                (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMinutes(30);
            throw new ImportException("Freelancehunt pieprasījumu limits sasniegts. Imports uz laiku apturēts.", delay > TimeSpan.FromMinutes(2) ? delay : TimeSpan.FromMinutes(2));
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new ImportException("Freelancehunt noraidīja API atslēgu (401). Pārbaudi lokāli saglabāto atslēgu.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new ImportException("Freelancehunt piekļuve liegta (403). Pārbaudi konta/API piekļuvi; nepērc abonementu automātiski.");
        if (!response.IsSuccessStatusCode)
            throw new ImportException($"Freelancehunt atbildēja ar HTTP {(int)response.StatusCode}. Imports nepabeigts.");
        // Never log the credential or server response body.
        string media = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!media.Contains("json", StringComparison.OrdinalIgnoreCase))
            throw new ImportException("Freelancehunt atgrieza lapu JSON vietā. Pārbaudi API piekļuvi.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }

    private static int? Count(JsonElement attributes, string field)
    {
        if (attributes.ValueKind != JsonValueKind.Object || !attributes.TryGetProperty(field, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int count) && count >= 0) return count;
        return null;
    }

    private static bool KnownUnavailable(JsonElement project, DateTime now, out string reason)
    {
        reason = "";
        if (!project.TryGetProperty("attributes", out var a) || a.ValueKind != JsonValueKind.Object) return false;
        if (a.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object &&
            status.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number &&
            id.TryGetInt32(out int statusId) && statusId != 11)
        {
            reason = "API statuss nav atvērts pieteikumiem";
            return true;
        }
        if (Flag(a, "is_only_for_plus") == true) { reason = "tikai Plus"; return true; }
        if (Flag(a, "is_personal") == true) { reason = "personisks projekts"; return true; }
        var expires = Date(a, "expired_at");
        if (expires.HasValue && expires <= now) { reason = "beidzies pieteikšanās termiņš"; return true; }
        return false;
    }

    private static bool Complete(JsonElement p) => p.TryGetProperty("attributes", out var a) &&
        a.TryGetProperty("status", out _) &&
        (!string.IsNullOrWhiteSpace(Text(a, "description")) || !string.IsNullOrWhiteSpace(Text(a, "description_html")));

    // Mirrors the candidate checks so the report explains exclusions without changing scoring thresholds.
    private static string CandidateRejectionReason(JsonElement project, DateTime now)
    {
        if (!project.TryGetProperty("attributes", out var a) || a.ValueKind != JsonValueKind.Object)
            return "trūkst attributes";
        int status = a.TryGetProperty("status", out var statusObj) && statusObj.ValueKind == JsonValueKind.Object &&
            statusObj.TryGetProperty("id", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out int id) ? id : 0;
        if (status != 11) return "nav atvērts pieteikumiem / nav statusa";
        if (Flag(a, "is_only_for_plus") == true) return "tikai Plus";
        if (Flag(a, "is_personal") == true) return "personisks projekts";
        string? raw = Text(a, "description");
        if (string.IsNullOrWhiteSpace(raw)) raw = Text(a, "description_html");
        int length = LeadContentText.ToPlainText(raw).Length;
        if (length < 30 || length > 60000) return "trūkst derīga apraksta";
        var published = Date(a, "published_at");
        if (!published.HasValue || published > now.AddMinutes(5) || published < now.AddDays(-30)) return "nav derīga publicēšanas datuma";
        var expires = Date(a, "expired_at");
        if (!expires.HasValue || expires <= now) return "termiņš beidzies vai nav datuma";
        return "cits iemesls";
    }

    public static RssLeadItem? ParseProject(JsonElement project, DateTime now) => ParseProject(project, now, out _);

    private static RssLeadItem? ParseProject(JsonElement project, DateTime now, out string? failure)
    {
        failure = null;
        if (Id(project) <= 0 || !project.TryGetProperty("attributes", out var a) || a.ValueKind != JsonValueKind.Object)
        { failure = "trūkst ID vai attributes objekta"; return null; }
        string title = LeadContentText.ToPlainText(Text(a, "name"));
        string? rawDescription = Text(a, "description");
        if (string.IsNullOrWhiteSpace(rawDescription)) rawDescription = Text(a, "description_html");
        string description = LeadContentText.ToPlainText(rawDescription);
        string? url = null;
        if (project.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object)
        {
            // API 2.0 project links are nested under self; retain support for flat fixtures/older feeds.
            if (links.TryGetProperty("self", out var self)) url = Text(self, "web");
            if (string.IsNullOrWhiteSpace(url)) url = Text(links, "web");
        }
        if (title.Length == 0 || title.Length > 1000) { failure = "trūkst derīga nosaukuma"; return null; }
        if (!IsProjectUrl(url)) { failure = "trūkst derīgas links.self.web projekta saites"; return null; }
        int status = a.TryGetProperty("status", out var statusObj) && statusObj.ValueKind == JsonValueKind.Object &&
            statusObj.TryGetProperty("id", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out int statusId) ? statusId : 0;
        var published = Date(a, "published_at"); var expires = Date(a, "expired_at");
        bool candidate = status == 11 && Flag(a, "is_only_for_plus") != true && Flag(a, "is_personal") != true &&
            description.Length is >= 30 and <= 60000 &&
            published.HasValue && published <= now.AddMinutes(5) && published >= now.AddDays(-30) &&
            expires.HasValue && expires > now;
        return new RssLeadItem { Title = title, Description = description, Url = url!, Source = "Freelancehunt",
            SourceType = "Advertisement", PublishedAt = published, IsCandidate = candidate };
    }

    public static bool IsProjectUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == "https" && u.IdnHost.Equals("freelancehunt.com", StringComparison.OrdinalIgnoreCase) &&
        u.IsDefaultPort && u.UserInfo.Length == 0 && u.Query.Length == 0 && u.Fragment.Length == 0 &&
        Regex.IsMatch(u.AbsolutePath, @"^/(?:en/|ua/|ru/)?project/[^/]+/[0-9]+\.html$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string? Text(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool? Flag(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;
    private static long Id(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("id", out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long id)) return id;
        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out id) ? id : 0;
    }
    private static DateTime? Date(JsonElement e, string name) => DateTimeOffset.TryParse(Text(e, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d.UtcDateTime : null;
    private sealed class ImportException(string message, TimeSpan? retryAfter = null) : Exception(message)
    { public TimeSpan? RetryAfter { get; } = retryAfter; }
}
