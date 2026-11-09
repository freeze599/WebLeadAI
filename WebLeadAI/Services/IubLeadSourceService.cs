using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebLeadAI.Services;

// Apzināti konservatīvs 14 dienu imports. Neskaidrus termiņus/versijas neimportē.
// Saglabātie ieraksti ir atraduma momentuzņēmumi; pirms pieteikšanās pārbauda EIS.
public sealed class IubLeadSourceService
{
    private readonly HttpClient _http;
    private readonly ILogger<IubLeadSourceService> _logger;
    public IubLeadSourceService(HttpClient http, ILogger<IubLeadSourceService> logger)
    { _http = http; _logger = logger; }

    private static JsonElement Get(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result)
            ? result : default;
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? "" : "";
    private static string S(JsonElement value, string name) => Text(Get(value, name));
    private static IEnumerable<JsonElement> Array(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    private static string Previous(JsonElement value)
    {
        var old = Get(value, "clonedFrom");
        return old.ValueKind == JsonValueKind.Object ? S(old, "identifier") : Text(old);
    }

    // Pieņem tikai zināmo EIS iepirkuma adresi; neseko patvaļīgām datu saitēm.
    private static string ProcurementUrl(JsonElement value)
    {
        foreach (var raw in new[] { S(Get(value, "tenderingTerms"), "submissionURL"),
                     S(Get(value, "tenderingProcess"), "documentsURL") })
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") ||
                !uri.Host.Equals("www.eis.gov.lv", StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(uri.AbsolutePath,
                @"^/EKEIS/Supplier/Procurement/([0-9]+)/?$", RegexOptions.IgnoreCase);
            if (match.Success) return "https://www.eis.gov.lv/EKEIS/Supplier/Procurement/" + match.Groups[1].Value;
        }
        return "";
    }

    private static bool IsWebsite(string text) => new[]
        { "mājaslap", "mājas lap", "tīmekļa vietn", "tīmekļvietn", "interneta vietn", "website" }
        .Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static bool OpenDeadline(JsonElement process, DateTimeOffset now,
        TimeZoneInfo zone, out DateTime deadline)
    {
        deadline = default;
        string date = S(process, "deadlineReceiptTendersEndDate");
        string time = S(process, "deadlineReceiptTendersEndTime");
        // Bez precīza laika atlase nav droša — izlaiž.
        if (!DateTime.TryParseExact(date + " " + time,
                new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy H:mm", "dd/MM/yyyy HH:mm:ss" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out deadline)) return false;
        deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(deadline) || zone.IsAmbiguousTime(deadline)) return false;
        return new DateTimeOffset(deadline, zone.GetUtcOffset(deadline)) > now;
    }

    // E-veidlapās summa mēdz būt skaitlis, teksts vai objekts ar amount/value.
    // Ja to nevar droši nolasīt, summu nerāda (neizdomā budžetu).
    private static decimal? Amount(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (string key in new[] { "amount", "value", "sum" })
            {
                var nested = Get(value, key);
                var parsed = Amount(nested);
                if (parsed.HasValue) return parsed;
            }
        }
        string raw = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : Text(value);
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            && number > 0 && number < 1_000_000_000_000m) return number;
        return null;
    }

    private static string ProcurementMetadata(JsonElement notice, IEnumerable<JsonElement> matchedLots)
    {
        var lots = matchedLots.ToList();
        // Vienai daļai tās plānotā vērtība ir tieši attiecināma uz atrasto darbu.
        // Vairāku daļu gadījumā vērtību neagregējam bez drošas deduplikācijas.
        decimal? estimated = lots.Count == 1 ? Amount(Get(Get(lots[0], "additionalInformation"), "estimatedValue")) : null;
        if (estimated == null) return "";
        return "\nParedzamā vērtība (publiskotie IUB dati): " +
            estimated.Value.ToString("N2", CultureInfo.GetCultureInfo("lv-LV")) + " EUR";
    }

    // Diagnostika: izdrukā tikai publiskajā JSON atrodamu budžeta lauku ceļus.
    // Lauku vērtības nelogo, lai žurnālu nepārblīvētu un neizpaustu liekus datus.
    private void LogBudgetPaths(JsonElement notice, string title)
    {
        var paths = new List<string>();
        void Walk(JsonElement node, string path, int depth)
        {
            if (depth > 12 || paths.Count >= 35) return;
            if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject())
                {
                    var next = path + "." + property.Name;
                    if (Regex.IsMatch(property.Name, "estimated|value|amount|budget|price|sum|cost", RegexOptions.IgnoreCase))
                        paths.Add(next + " (" + property.Value.ValueKind + ")");
                    Walk(property.Value, next, depth + 1);
                    if (paths.Count >= 35) break;
                }
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var child in node.EnumerateArray())
                {
                    Walk(child, path + "[" + i + "]", depth + 1);
                    if (++i >= 8 || paths.Count >= 35) break;
                }
            }
        }
        Walk(notice, "$", 0);
        _logger.LogWarning("IUB BUDZETA DIAGNOSTIKA: {Title} | atrastie lauki: {Paths}",
            title, paths.Count == 0 ? "NAV; jāpārbauda vai dienas JSON vispār satur summu" : string.Join("; ", paths));
    }

    public async Task<RssFeedResult> GetFeedResultAsync()
    {
        var result = new RssFeedResult { Source = "IUB" };
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "FLE Standard Time" : "Europe/Riga");
            var now = DateTimeOffset.UtcNow;
            var today = TimeZoneInfo.ConvertTime(now, zone).Date;
            var notices = new List<(DateTime Day, JsonElement Data)>();
            int downloaded = 0;
            // Neģenerē iespējami neeksistējošu dienu adreses: IUB tām var
            // atdot HTML ar HTTP 200. Izmanto tikai katalogā publicētas saites.
            var firstDay = today.AddDays(-13);
            var files = new SortedDictionary<DateTime, string>();
            var months = Enumerable.Range(0, 14).Select(offset => today.AddDays(-offset)
                .ToString("yyyy/MM", CultureInfo.InvariantCulture)).Distinct();
            foreach (string month in months)
            {
                string directoryUrl = "https://open.iub.gov.lv/data/notice/" + month;
                using var directoryResponse = await _http.GetAsync(directoryUrl, budget.Token);
                directoryResponse.EnsureSuccessStatusCode();
                string html = await directoryResponse.Content.ReadAsStringAsync(budget.Token);
                var links = Regex.Matches(html,
                    "href=[\"'](?<path>/data/notice/[0-9]{4}/[0-9]{2}/(?<date>[0-9]{2}-[0-9]{2}-[0-9]{4})\\.json)[\"']",
                    RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
                if (links.Count == 0)
                    throw new JsonException("IUB katalogā nav atpazīstamu JSON saišu: " + directoryUrl);
                foreach (Match link in links)
                {
                    if (!DateTime.TryParseExact(link.Groups["date"].Value, "dd-MM-yyyy",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
                    string expected = "/data/notice/" + day.ToString("yyyy/MM/dd-MM-yyyy", CultureInfo.InvariantCulture) + ".json";
                    if (day < firstDay || day > today ||
                        day.ToString("yyyy/MM", CultureInfo.InvariantCulture) != month ||
                        link.Groups["path"].Value != expected) continue;
                    files[day] = "https://open.iub.gov.lv" + expected;
                }
            }
            foreach (var file in files)
            {
                _logger.LogInformation("IUB dienas fails: {Url}", file.Value);
                using var response = await _http.GetAsync(file.Value, budget.Token);
                response.EnsureSuccessStatusCode();
                string body = (await response.Content.ReadAsStringAsync(budget.Token))
                    .TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
                if (!body.StartsWith("[", StringComparison.Ordinal))
                    throw new JsonException("IUB dienas fails neatgrieza JSON masīvu: " + file.Value +
                        "; Content-Type: " + response.Content.Headers.ContentType);
                using var json = JsonDocument.Parse(body);
                downloaded++;
                notices.AddRange(json.RootElement.EnumerateArray().Select(x => (file.Key, x.Clone())));
            }
            if (downloaded == 0) throw new HttpRequestException("Nav pieejams neviens IUB dienas fails.");

            var replaced = notices.Select(x => Previous(x.Data)).Where(x => x.Length > 0).ToHashSet();
            var latest = notices.Where(x => !replaced.Contains(S(x.Data, "identifier")))
                .GroupBy(x => S(x.Data, "procurementProcedureIdentifier") is { Length: > 0 } id
                    ? id : ProcurementUrl(x.Data));
            var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in latest)
            {
                if (group.Key.Length == 0) continue;
                var lastDay = group.Max(x => x.Day);
                var last = group.Where(x => x.Day == lastDay).ToList();
                // Vairāki nesaistīti paziņojumi vienā dienā: secība nav zināma.
                if (last.Count != 1) continue;
                var notice = last[0].Data;
                if (S(notice, "formType") != "competition") continue;
                string url = ProcurementUrl(notice);
                string title = LeadContentText.ToPlainText(S(notice, "name"));
                if (url.Length == 0 || title.Length == 0) continue;
                string description = LeadContentText.ToPlainText(S(Get(notice, "procurementProject"), "description"));
                var active = new List<string>();
                var matchedLots = new List<JsonElement>();
                foreach (var lot in Array(Get(notice, "lots")))
                {
                    string lotText = LeadContentText.ToPlainText(S(lot, "name") + "\n" + S(lot, "description"));
                    if (!IsWebsite(lotText) && !IsWebsite(title + "\n" + description)) continue;
                    if (!OpenDeadline(Get(lot, "tenderingProcess"), DateTimeOffset.UtcNow, zone, out var deadline)) continue;
                    matchedLots.Add(lot);
                    active.Add(lotText + "\nPieteikšanās termiņš (Latvijas laiks): " +
                        deadline.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture));
                }
                if (active.Count == 0 || !usedUrls.Add(url)) continue;
                // Ja summa nav iegūta, vienreiz izdrukā attiecīgā paziņojuma
                // budžetam līdzīgo lauku ceļus, lai varētu salabot parseri pēc datiem.
                if (ProcurementMetadata(notice, matchedLots).Length == 0)
                    LogBudgetPaths(notice, title);
                result.Items.Add(new RssLeadItem
                {
                    Title = title, Url = url, Source = "IUB",
                    // Dienas faila datums nav precīzs publicēšanas laiks.
                    PublishedAt = null,
                    Description = description + "\n\n" + string.Join("\n\n", active) +
                        "\n\nPasūtītājs: " + LeadContentText.ToPlainText(S(Get(notice, "organizationData"), "name")) +
                        ProcurementMetadata(notice, matchedLots) +
                        "\nIUB dienas fails: " + lastDay.ToString("dd.MM.yyyy") +
                        "\nPublisks iepirkums. Pirms pieteikšanās EIS pārbaudi aktuālo termiņu, prasības un grozījumus."
                });
            }
            _logger.LogInformation("IUB: {Notices} paziņojumi, {Candidates} vietņu kandidāti.", notices.Count, result.Items.Count);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException
                                       or TimeZoneNotFoundException or InvalidTimeZoneException or RegexMatchTimeoutException)
        {
            // Nepilnu dienu kopu neimportē: tajā var trūkt jaunākais labojums.
            result.Items.Clear();
            result.Error = "IUB datus neizdevās pilnībā pārbaudīt. Mēģini vēlāk.";
            _logger.LogWarning(ex, "IUB imports izlaists.");
        }
        return result;
    }
}
