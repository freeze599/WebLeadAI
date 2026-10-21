
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebLeadAI.Data;
using WebLeadAI.Models;
using WebLeadAI.Services;

namespace WebLeadAI.Controllers
{
    public class LeadsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly LeadDiscoveryService _discoveryService;

        public LeadsController(
            AppDbContext context,
            LeadDiscoveryService discoveryService)
        {
            _context = context;
            _discoveryService = discoveryService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? status,
            string? source,
            string? sort)
        {
            var allLeads = await _context.Leads
                .AsNoTracking()
                .ToListAsync();

            ViewBag.TotalCount = allLeads.Count;
            ViewBag.ReviewCount = allLeads.Count(l =>
                l.Status == "NeedsReview" || l.Status == "New");
            ViewBag.QualifiedCount = allLeads.Count(l =>
                l.Status == "Qualified");
            ViewBag.RejectedCount = allLeads.Count(l =>
                l.Status == "Rejected");

            ViewBag.Sources = allLeads
                .Select(GetSource)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            ViewBag.Search = search ?? "";
            ViewBag.Status = status ?? "";
            ViewBag.Source = source ?? "";
            ViewBag.Sort = sort ?? "score";

            IEnumerable<Lead> leads = allLeads;

            if (!string.IsNullOrWhiteSpace(search))
            {
                leads = leads.Where(l =>
                    l.CompanyName.Contains(
                        search,
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    (l.Description?.Contains(
                        search,
                        StringComparison.OrdinalIgnoreCase) ?? false));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                leads = leads.Where(l => l.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(source))
            {
                leads = leads.Where(l =>
                    GetSource(l).Equals(
                        source,
                        StringComparison.OrdinalIgnoreCase));
            }

            leads = sort switch
            {
                "newest" => leads
                    .OrderByDescending(l => l.CreatedAt),

                "oldest" => leads
                    .OrderBy(l => l.CreatedAt),

                "score_asc" => leads
                    .OrderBy(l => l.AILeadScore)
                    .ThenByDescending(l => l.CreatedAt),

                _ => leads
                    .OrderByDescending(l => l.AILeadScore)
                    .ThenByDescending(l => l.CreatedAt)
            };

            return View(leads.ToList());
        }

        private static string GetSource(Lead lead)
        {
            if (string.IsNullOrWhiteSpace(lead.SourceUrl))
                return "Manual";

            if (Uri.TryCreate(
                lead.SourceUrl,
                UriKind.Absolute,
                out var uri))
            {
                return uri.Host
                    .Replace("www.", "")
                    .ToLowerInvariant();
            }

            return "Unknown";
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var lead = await _context.Leads
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id);

            if (lead == null)
                return NotFound();

            return View(lead);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new Lead());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("CompanyName,Website,Email,SourceUrl,SourceType,Description")]
            Lead lead)
        {
            if (!ModelState.IsValid)
                return View(lead);

            lead.Status = "New";
            lead.AILeadScore = 0;
            lead.CreatedAt = DateTime.UtcNow;

            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();

            TempData["Success"] =
                "Klients veiksmīgi pievienots!";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Discover()
        {
            int added =
                await _discoveryService.DiscoverLeadsAsync();

            TempData["Success"] =
                $"Meklēšana pabeigta. Pievienoti {added} jauni projekti.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(
            int id,
            string status)
        {
            string[] allowedStatuses =
            {
                "NeedsReview",
                "Qualified",
                "Rejected"
            };

            if (!allowedStatuses.Contains(status))
                return BadRequest("Nederīgs statuss.");

            var lead = await _context.Leads
                .FirstOrDefaultAsync(l => l.Id == id);

            if (lead == null)
                return NotFound();

            lead.Status = status;

            await _context.SaveChangesAsync();

            TempData["Success"] =
                "Projekta statuss veiksmīgi atjaunināts.";

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecalculateScores(
            [FromServices] LeadQualificationService qualification)
        {
            var leads = await _context.Leads.ToListAsync();

            foreach (var lead in leads)
            {
                var result = qualification.Evaluate(
                    lead.CompanyName,
                    lead.Description ?? "");

                lead.AILeadScore = result.Score;

                // Pārvērtējam punktus, bet nemainām
                // iepriekš noteiktos statusus.
            }

            await _context.SaveChangesAsync();

            TempData["Success"] =
                $"Pārvērtēti {leads.Count} projekti. Statusi saglabāti.";

            return RedirectToAction(nameof(Index));
        }
    }
}
