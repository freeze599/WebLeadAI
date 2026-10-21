
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
        public async Task<IActionResult> Index()
        {
            var leads = await _context.Leads
                .AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return View(leads);
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
                $"Meklēšana pabeigta. Pievienoti {added} jauni klienti.";

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
                $"Projekta statuss mainīts uz {status}.";

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

            int updated = 0;

            foreach (var lead in leads)
            {
                var result = qualification.Evaluate(
                    lead.CompanyName,
                    lead.Description ?? "");

                lead.AILeadScore = result.Score;

                // Manuāli pieņemtos lēmumus nemainām.
                if (lead.Status != "Qualified" &&
                    lead.Status != "Rejected" &&
                    lead.Status != "Contacted")
                {
                    lead.Status = result.IsQualified
                        ? "NeedsReview"
                        : "Rejected";
                }

                updated++;
            }

            await _context.SaveChangesAsync();

            TempData["Success"] =
                $"Pārvērtēti {updated} projekti.";

            return RedirectToAction(nameof(Index));
        }

    } }
