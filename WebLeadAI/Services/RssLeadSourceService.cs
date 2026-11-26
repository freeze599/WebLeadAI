
using System.ServiceModel.Syndication;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;

namespace WebLeadAI.Services
{
    public class RssLeadSourceService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RssLeadSourceService> _logger;

        // Tikai pārbaudīta publiska RSS plūsma; maksas platformu imports izslēgts.
        private readonly Dictionary<string, string> _feeds = new()
        {
            { "SS.com", "https://www.ss.com/lv/work/are-required/programmer/rss/" }
        };
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static List<RssFeedResult>? _cached;
        private static DateTime _cacheUntil;

        public RssLeadSourceService(
            HttpClient httpClient,
            ILogger<RssLeadSourceService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<List<RssLeadItem>> GetLeadsAsync()
        {
            var feeds = await GetFeedResultsAsync();
            return feeds.SelectMany(f => f.Items).ToList();
        }

        public async Task<List<RssFeedResult>> GetFeedResultsAsync()
        {
            await Gate.WaitAsync();
            try
            {
                if (_cached != null && DateTime.UtcNow < _cacheUntil) return _cached;
                var results = await FetchFeedsAsync();
                _cached = results;
                _cacheUntil = DateTime.UtcNow.AddMinutes(results.Any(x => x.Error != null) ? 2 : 30);
                return results;
            }
            finally { Gate.Release(); }
        }

        private async Task<List<RssFeedResult>> FetchFeedsAsync()
        {
            var results = new List<RssFeedResult>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            foreach (var feed in _feeds)
            {
                var feedResult = new RssFeedResult { Source = feed.Key };
                results.Add(feedResult);

                try
                {
                    _logger.LogInformation(
                        "Sāk RSS avota pārbaudi: {Source}",
                        feed.Key);

                    using var response =
                        await _httpClient.GetAsync(feed.Value, deadline.Token);

                    response.EnsureSuccessStatusCode();

                    await using var stream =
                        await response.Content.ReadAsStreamAsync();

                    using var reader = XmlReader.Create(
                        stream,
                        new XmlReaderSettings
                        {
                            DtdProcessing = DtdProcessing.Prohibit,
                            XmlResolver = null,
                            MaxCharactersInDocument = 5_000_000
                        });

                    var syndicationFeed =
                        SyndicationFeed.Load(reader);

                    if (syndicationFeed == null)
                        throw new InvalidOperationException("Avots neatgrieza RSS plūsmu.");

                    int count = 0;

                    foreach (var item in syndicationFeed.Items.Take(20))
                    {
                        string title = LeadContentText.ToPlainText(item.Title?.Text);
                        string rawDescription = item.Summary?.Text
                            ?? (item.Content as TextSyndicationContent)?.Text
                            ?? "";
                        string description = LeadContentText.ToPlainText(rawDescription);

                        string url = item.Links
                            .FirstOrDefault(l =>
                                l.RelationshipType == "alternate")
                            ?.Uri.ToString() ?? "";

                        if (string.IsNullOrWhiteSpace(title) ||
                            string.IsNullOrWhiteSpace(url))
                            continue;

                        if (!Uri.TryCreate(
                                url,
                                UriKind.Absolute,
                                out var uri) ||
                            (uri.Scheme != Uri.UriSchemeHttps &&
                             uri.Scheme != Uri.UriSchemeHttp))
                            continue;

                        if (url.Length > 500)
                            continue;

                        if (!SsLeadReader.IsAllowedUrl(url)) continue;
                        try
                        {
                            await Task.Delay(1000, deadline.Token);
                            using var detail = await _httpClient.GetAsync(url, deadline.Token);
                            detail.EnsureSuccessStatusCode();
                            if (!SsLeadReader.IsAllowedUrl(detail.RequestMessage?.RequestUri?.ToString() ?? ""))
                                throw new InvalidOperationException("SS.com pāradresācija ārpus atļautās kategorijas.");
                            description = SsLeadReader.ReadDescription(await detail.Content.ReadAsStringAsync());
                        }
                        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or RegexMatchTimeoutException)
                        {
                            feedResult.Error = "Daļu SS.com pilno tekstu neizdevās nolasīt; šie ieraksti izlaisti.";
                            _logger.LogWarning(ex, "SS.com teksts nav pieejams: {Url}", url);
                            if (deadline.IsCancellationRequested) break;
                            if (ex is HttpRequestException h && h.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) break;
                            continue;
                        }

                        DateTime? publishedAt = null;

                        if (item.PublishDate != DateTimeOffset.MinValue)
                        {
                            publishedAt = item.PublishDate.UtcDateTime;
                        }

                        feedResult.Items.Add(new RssLeadItem
                        {
                            Title = title,
                            Description = description,
                            Url = url,
                            Source = feed.Key,
                            PublishedAt = publishedAt
                        });

                        count++;
                    }

                    _logger.LogInformation(
                        "RSS avots {Source}: nolasīti {Count} ieraksti.",
                        feed.Key,
                        count);
                }
                catch (Exception ex) when (
                    ex is HttpRequestException ||
                    ex is XmlException ||
                    ex is TaskCanceledException ||
                    ex is InvalidOperationException)
                {
                    feedResult.Error = ex.Message;
                    _logger.LogWarning(
                        ex,
                        "RSS avots {Source} nav pieejams.",
                        feed.Key);
                }
            }

            return results;
        }
    }

    public class RssLeadItem
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Url { get; set; } = "";
        public string Source { get; set; } = "";
        public string SourceType { get; set; } = "Advertisement";
        public bool IsCandidate { get; set; } = true;
        public WebLeadAI.Models.JobicyItemDiagnostics? JobicyDiagnostics { get; set; }
        public DateTime? PublishedAt { get; set; }
    }
}

namespace WebLeadAI.Services
{
    public class RssFeedResult
    {
        public string Source { get; set; } = "";
        public List<RssLeadItem> Items { get; set; } = new();
        public string? Error { get; set; }
        public string? Notice { get; set; }
        public DateTime? FetchedAtUtc { get; set; }
    }
}


namespace WebLeadAI.Services
{
    // Kopīga apstrāde RSS importam, detaļu skatam un algoritmam 2.x.
    // Rezultāts ir parasts teksts. Razor skatā to izvada bez Html.Raw.
    public static class LeadContentText
    {
        private static string Replace(string text, string pattern, string replacement)
        {
            return Regex.Replace(text, pattern, replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }

        public static string ToPlainText(string? value)
        {
            string text = value ?? "";
            // Atkodē arī atkārtoti kodētus RSS fragmentus.
            for (int i = 0; i < 3; i++)
            {
                string decoded = WebUtility.HtmlDecode(text);
                if (decoded == text)
                    break;
                text = decoded;
            }

            text = Replace(text, @"<!--[^>]*?-->", " ");
            text = Replace(text, @"<(script|style)\b[^>]*>[\s\S]*?</\1\s*>", " ");
            text = Replace(text, @"<br\b[^>]*>|</?(?:p|div|li|ul|ol|h[1-6]|tr|section)\b[^>]*>", "\n");
            // Neaiztiek parastu tekstu, piemēram, 'budget < 500'.
            text = Replace(text, @"</?[a-z][^>]*>", " ");
            text = text.Replace('\u00a0', ' ').Replace("\r\n", "\n").Replace('\r', '\n');
            text = Replace(text, @"[^\S\n]+", " ");
            text = Replace(text, @" *\n *", "\n");
            text = Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }

        public static string ForEvaluation(string? value)
        {
            string text = ToPlainText(value);

            // Codeur RSS: atsevišķa budžeta/kategoriju rindkopa, nevis klienta prasības.
            // Dzēš tikai pazīstamu rindas formātu; neskaidru tekstu saglabā.
            string withoutHeader = Replace(text,
                @"(?m)^Budget\s*:[^\n]*?\s[-–—]\s*Cat[eé]gories\s*:[^\n]*(?:\n|$)", "");
            if (!string.IsNullOrWhiteSpace(withoutHeader))
                text = withoutHeader;

            text = Replace(text,
                @"(?m)^Voir ce projet sur Codeur\s*\.?\s*$", "");

            // Freelancer RSS beigās pievieno budžetu un 'Jobs:' birkas.
            // Tās neapstiprina, ka visas uzskaitītās tehnoloģijas ir obligātas.
            text = Replace(text,
                @"\s*\(Budget\s*:[^()]*,\s*Jobs\s*:[^()]*\)\s*$", "");
            return text.Trim();
        }
    }
}
