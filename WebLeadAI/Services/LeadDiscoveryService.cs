
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
        private readonly IubLeadSourceService _iub;
        private readonly ILogger<LeadDiscoveryService> _logger;

        public LeadDiscoveryService(
            AppDbContext context,
            RssLeadSourceService rssService,
            LeadQualificationService qualification,
            IubLeadSourceService iub,
            ILogger<LeadDiscoveryService> logger)
        {
            _context = context;
            _rssService = rssService;
            _qualification = qualification;
            _iub = iub;
            _logger = logger;
        }

        public async Task<int> DiscoverLeadsAsync()
        {
            var report = await DiscoverWithStatisticsAsync();
            return report.Sum(x => x.Added);
        }

        public async Task<List<LeadSourceStatistics>> DiscoverWithStatisticsAsync()
        {
            var feeds = await _rssService.GetFeedResultsAsync();
            feeds.Add(await _iub.GetFeedResultAsync());
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

                    // Jaunais vērtēšanas algoritms 2.3
                    var evaluation = _qualification.EvaluateV2(
                        item.Title,
                        item.Description);

                    if (feed.Source == "IUB")
                    {
                        _logger.LogInformation(
                            "IUB KANDIDĀTS: {Title} | Atbilstošs: {Qualified} | Prioritāte: {Score} | Pamatojums: {Reason} | Saite: {Url}",
                            item.Title.Replace('\r', ' ').Replace('\n', ' '),
                            evaluation.IsQualified, evaluation.Score,
                            evaluation.Reason, item.Url);
                    }

                    if (evaluation.IsQualified)
                        stats.Qualified++;

                    if (knownUrls.TryGetValue(
                        item.Url,
                        out var existingLead))
                    {
                        stats.Duplicates++;

                        // Papildina trūkstošu publicēšanas datumu.
                        // Nemaina statusu un iepriekšējo vērtējumu.
                        if (existingLead.PublishedAt == null &&
                            item.PublishedAt != null)
                        {
                            existingLead.PublishedAt = item.PublishedAt;
                        }

                        // Ja IUB ir publicējis paredzamo vērtību, papildina esošo ierakstu.
                        // Nekad nepārraksta lietotāja statusu, vērtējumu vai melnrakstu.
                        const string budgetLabel = "Paredzamā vērtība (publiskotie IUB dati): ";
                        if (feed.Source == "IUB" &&
                            item.Description.Contains(budgetLabel, StringComparison.Ordinal) &&
                            !(existingLead.Description ?? "").Contains(budgetLabel, StringComparison.Ordinal))
                        {
                            var line = item.Description.Split('\n')
                                .FirstOrDefault(x => x.StartsWith(budgetLabel, StringComparison.Ordinal));
                            if (!string.IsNullOrWhiteSpace(line))
                                existingLead.Description = (existingLead.Description ?? "").TrimEnd() +
                                    "\n" + line;
                        }

                        // Dublikātu gadījumā papildina tikai trūkstošus strukturētos laukus.
                        ProcurementFields.FillMissing(existingLead, item.Description);
                        ProcurementFields.FillMissing(existingLead, existingLead.Description);
                        continue;
                    }

                    if (!evaluation.IsQualified)
                    {
                        stats.Rejected++;
                        continue;
                    }

                    string title = item.Title.Length > 200
                        ? item.Title[..200]
                        : item.Title;

                    var lead = new Lead
                    {
                        CompanyName = title,
                        SourceUrl = item.Url,
                        SourceType = feed.Source == "IUB" ? "Procurement" : "Advertisement",
                        Description = item.Description,
                        Status = "NeedsReview",
                        AILeadScore = evaluation.Score,
                        CreatedAt = DateTime.UtcNow,
                        PublishedAt = item.PublishedAt
                    };

                    ProcurementFields.FillMissing(lead, item.Description);
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
