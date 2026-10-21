
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
            var items = await _rssService.GetLeadsAsync();

            int added = 0;

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

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Url))
                    continue;

                if (knownUrls.TryGetValue(
                    item.Url,
                    out var existingLead))
                {
                    // Papildinām datumu, bet nepārrakstām
                    // lietotāja noteikto statusu.
                    if (existingLead.PublishedAt == null &&
                        item.PublishedAt != null)
                    {
                        existingLead.PublishedAt = item.PublishedAt;
                    }

                    continue;
                }

                var result = _qualification.Evaluate(
                    item.Title,
                    item.Description);

                if (!result.IsQualified)
                    continue;

                string title = item.Title.Length > 200
                    ? item.Title[..200]
                    : item.Title;

                var lead = new Lead
                {
                    CompanyName = title,
                    SourceUrl = item.Url,
                    SourceType = "Advertisement",
                    Description =
                        $"Avots: {item.Source}\n" +
                        $"Atlase: {result.Reason}\n\n" +
                        item.Description,
                    Status = "NeedsReview",
                    AILeadScore = result.Score,
                    CreatedAt = DateTime.UtcNow,
                    PublishedAt = item.PublishedAt
                };

                _context.Leads.Add(lead);
                knownUrls[item.Url] = lead;

                added++;
            }

            await _context.SaveChangesAsync();

            return added;
        }
    }
}
