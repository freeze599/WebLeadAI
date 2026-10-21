
using System.ServiceModel.Syndication;
using System.Xml;

namespace WebLeadAI.Services
{
    public class RssLeadSourceService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RssLeadSourceService> _logger;

        private readonly Dictionary<string, string> _feeds = new()
        {
            {
                "Codeur",
                "https://www.codeur.com/projects?format=rss"
            },
            {
                "FreelanceUA",
                "https://freelance.ua/orders/rss?t=1&cat_id=17&sub_id=0"
            }
        };

        public RssLeadSourceService(
            HttpClient httpClient,
            ILogger<RssLeadSourceService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<List<RssLeadItem>> GetLeadsAsync()
        {
            var results = new List<RssLeadItem>();

            foreach (var feed in _feeds)
            {
                try
                {
                    using var response =
                        await _httpClient.GetAsync(feed.Value);

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
                        continue;

                    foreach (var item in syndicationFeed.Items)
                    {
                        string title = item.Title?.Text ?? "";
                        string description = item.Summary?.Text ?? "";

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

                        DateTime? publishedAt = null;

                        if (item.PublishDate != DateTimeOffset.MinValue)
                        {
                            publishedAt = item.PublishDate.UtcDateTime;
                        }

                        results.Add(new RssLeadItem
                        {
                            Title = title,
                            Description = description,
                            Url = url,
                            Source = feed.Key,
                            PublishedAt = publishedAt
                        });
                    }

                    _logger.LogInformation(
                        "RSS avots {Source}: nolasīšana pabeigta.",
                        feed.Key);
                }
                catch (Exception ex) when (
                    ex is HttpRequestException ||
                    ex is XmlException ||
                    ex is TaskCanceledException ||
                    ex is InvalidOperationException)
                {
                    _logger.LogWarning(
                        ex,
                        "Neizdevās nolasīt RSS avotu {Source}",
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

        public DateTime? PublishedAt { get; set; }
    }
}
