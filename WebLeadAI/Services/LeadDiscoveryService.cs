
using Microsoft.EntityFrameworkCore;
using WebLeadAI.Data;
using WebLeadAI.Models;

namespace WebLeadAI.Services
{
    public class LeadDiscoveryService
    {
        private readonly AppDbContext _context;
        private readonly RssLeadSourceService _rssService;
        private readonly LeadQualificationService _qualification;

        public LeadDiscoveryService(
            AppDbContext context,
            RssLeadSourceService rssService,
            LeadQualificationService qualification)
        {
            _context = context;
            _rssService = rssService;
            _qualification = qualification;
        }

        public async Task<int> DiscoverLeadsAsync()
        {
            var report = await DiscoverWithStatisticsAsync();
            return report.Sum(x => x.Added);
        }

        public async Task<List<LeadSourceStatistics>>
            DiscoverWithStatisticsAsync()
        {
            var feeds = await _rssService.GetFeedResultsAsync();
            var report = new List<LeadSourceStatistics>();

            var existingLeads = await _context.Leads
                .Where(l => l.SourceUrl != null)
                .ToListAsync();

            var knownUrls = existingLeads
                .GroupBy(
                    l => l.SourceUrl!,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.First(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var feed in feeds)
            {
                var stats = new LeadSourceStatistics
                {
                    Source = feed.Source,
                    Read = feed.Items.Count,
                    Error = feed.Error
                };

                report.Add(stats);

                foreach (var item in feed.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Url))
                        continue;

                    if (knownUrls.TryGetValue(
                        item.Url,
                        out var existingLead))
                    {
                        stats.Duplicates++;

                        if (existingLead.PublishedAt == null &&
                            item.PublishedAt != null)
                        {
                            existingLead.PublishedAt =
                                item.PublishedAt;
                        }

                        continue;
                    }

                    var evaluation = _qualification.Evaluate(
                        item.Title,
                        item.Description);

                    if (!evaluation.IsQualified)
                    {
                        stats.Rejected++;
                        continue;
                    }

                    stats.Qualified++;

                    string title = item.Title.Length > 200
                        ? item.Title[..200]
                        : item.Title;

                    var lead = new Lead
                    {
                        CompanyName = title,
                        SourceUrl = item.Url,
                        SourceType = "Advertisement",

                        // Saglabājam tikai oriģinālo aprakstu.
                        Description = item.Description,

                        Status = "NeedsReview",
                        AILeadScore = evaluation.Score,
                        CreatedAt = DateTime.UtcNow,
                        PublishedAt = item.PublishedAt
                    };

                    _context.Leads.Add(lead);
                    knownUrls[item.Url] = lead;

                    stats.Added++;
                }
            }

            await _context.SaveChangesAsync();

            return report;
        }
    }

    public class LeadSourceStatistics
    {
        public string Source { get; set; } = "";
        public int Read { get; set; }
        public int Qualified { get; set; }
        public int Duplicates { get; set; }
        public int Rejected { get; set; }
        public int Added { get; set; }
        public string? Error { get; set; }
    }
}
