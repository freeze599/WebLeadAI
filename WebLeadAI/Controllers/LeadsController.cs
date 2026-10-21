
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

        // =====================================
        // 1. KLIENTU SARAKSTS
        // =====================================

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var leads = await _context.Leads
                .AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return View(leads);
        }

        // =====================================
        // 2. JAUNA KLIENTA PIEVIENOŠANAS FORMA
        // =====================================

        [HttpGet]
        public IActionResult Create()
        {
            return View(new Lead());
        }

        // =====================================
        // 3. JAUNA KLIENTA SAGLABĀŠANA
        // =====================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind(
                "CompanyName,Website,Email,SourceUrl,SourceType,Description"
            )] Lead lead)
        {
            if (!ModelState.IsValid)
            {
                return View(lead);
            }

            lead.Status = "New";
            lead.AILeadScore = 0;
            lead.CreatedAt = DateTime.UtcNow;

            _context.Leads.Add(lead);

            await _context.SaveChangesAsync();

            TempData["Success"] =
                "Klients veiksmīgi pievienots!";

            return RedirectToAction(nameof(Index));
        }

        // =====================================
        // 4. AUTOMĀTISKĀ KLIENTU MEKLĒŠANA
        // =====================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Discover()
        {
            int added =
                await _discoveryService.DiscoverLeadsAsync();

            TempData["Success"] =
                $"Meklēšana pabeigta. " +
                $"Pievienoti {added} jauni klienti.";

            return RedirectToAction(nameof(Index));
        }
    }
}
