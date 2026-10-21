
using System.ServiceModel.Syndication;
using System.Xml;

namespace WebLeadAI.Services
{
    public class RssLeadSourceService
    {
        private readonly HttpClient _httpClient;

        private const string FeedUrl =
            "https://weworkremotely.com/categories/remote-full-stack-programming-jobs.rss";

        public RssLeadSourceService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<RssLeadItem>> GetLeadsAsync()
        {
            var results = new List<RssLeadItem>();

            using var response =
                await _httpClient.GetAsync(FeedUrl);

            response.EnsureSuccessStatusCode();

            await using var stream =
                await response.Content.ReadAsStreamAsync();

            using var reader = XmlReader.Create(stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });

            var feed = SyndicationFeed.Load(reader);

            if (feed == null)
                return results;

            foreach (var item in feed.Items)
            {
                string title = item.Title?.Text ?? "";

                string description =
                    item.Summary?.Text ?? "";

                string url = item.Links
                    .FirstOrDefault(l =>
                        l.RelationshipType == "alternate")
                    ?.Uri.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(url))
                    continue;

                results.Add(new RssLeadItem
                {
                    Title = title,
                    Description = description,
                    Url = url
                });
            }

            return results;
        }
    }

    public class RssLeadItem
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Url { get; set; } = "";
    }
}
