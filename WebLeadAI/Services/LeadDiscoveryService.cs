
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

            

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Url))
                    continue;

                var result = _qualification.Evaluate(
                    item.Title,
                    item.Description);

                // Saglabājam tikai iespējamos pasūtījumus
                if (!result.IsQualified)
                    continue;

                bool exists = await _context.Leads
                    .AnyAsync(l => l.SourceUrl == item.Url);

                if (exists)
                    continue;

                var lead = new Lead
                {
                    CompanyName = item.Title.Length > 200
                        ? item.Title[..200]
                        : item.Title,

                    SourceUrl = item.Url,
                    SourceType = "Advertisement",
                    Description = item.Description,
                    Status = "NeedsReview",
                    AILeadScore = result.Score,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Leads.Add(lead);
                added++;
            }

            await _context.SaveChangesAsync();

            return added;
        }
    }
}
