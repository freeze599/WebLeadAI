
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using WebLeadAI.Data;

namespace WebLeadAI.Services
{
    public class LeadDescriptionCleanupService
    {
        private readonly AppDbContext _context;

        public LeadDescriptionCleanupService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<LeadCleanupResult> CleanAsync(
            bool applyChanges = false)
        {
            var leads = await _context.Leads.ToListAsync();

            var result = new LeadCleanupResult
            {
                Total = leads.Count
            };

            foreach (var lead in leads)
            {
                if (string.IsNullOrWhiteSpace(lead.Description))
                    continue;

                string original = lead.Description;

                // Atpazīstam tikai mūsu agrāko formātu:
                // Avots: ...
                // Atlase: ...
                //
                // Oriģinālais apraksts

                var match = Regex.Match(
                    original,
                    @"\AAvots:[^\r\n]*\r?\nAtlase:[^\r\n]*\r?\n(?:[ \t]*\r?\n)+(.*)\z",
                    RegexOptions.Singleline |
                    RegexOptions.CultureInvariant);

                if (!match.Success)
                    continue;

                string cleaned = match.Groups[1].Value;

                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    result.Skipped++;
                    continue;
                }

                result.Matched++;

                if (applyChanges)
                {
                    lead.Description = cleaned;
                    result.Cleaned++;
                }
            }

            if (applyChanges && result.Cleaned > 0)
                await _context.SaveChangesAsync();

            return result;
        }
    }

    public class LeadCleanupResult
    {
        public int Total { get; set; }
        public int Matched { get; set; }
        public int Cleaned { get; set; }
        public int Skipped { get; set; }
    }
}
