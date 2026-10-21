
using Microsoft.EntityFrameworkCore;
using WebLeadAI.Data;
using WebLeadAI.Models;

namespace WebLeadAI.Services
{
    public class LeadDiscoveryService
    {
        private readonly AppDbContext _context;
        private readonly RssLeadSourceService _rssService;

        public LeadDiscoveryService(
            AppDbContext context,
            RssLeadSourceService rssService)
        {
            _context = context;
            _rssService = rssService;
        }

        public async Task<int> DiscoverLeadsAsync()
        {
            var items = await _rssService.GetLeadsAsync();

            int added = 0;

            string[] keywords =
            {
                "website development",
                "web developer",
                "web design",
                "asp.net",
                "full stack",
                "full-stack",
                "frontend",
                "front-end"
            };

            foreach (var item in items)
            {
                string content =
                    (item.Title + " " + item.Description)
                    .ToLowerInvariant();

                bool relevant = keywords.Any(keyword =>
                    content.Contains(keyword));

                if (!relevant)
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
                    Status = "New",
                    AILeadScore = 0,
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
