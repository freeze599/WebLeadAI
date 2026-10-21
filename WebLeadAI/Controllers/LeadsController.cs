
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebLeadAI.Data;
using WebLeadAI.Models;

namespace WebLeadAI.Controllers
{
    public class LeadsController : Controller
    {
        private readonly AppDbContext _context;

        public LeadsController(AppDbContext context)
        {
            _context = context;
        }

        // Klientu saraksts
        public async Task<IActionResult> Index()
        {
            var leads = await _context.Leads
                .AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return View(leads);
        }

        // Klienta pievienošanas forma
        [HttpGet]
        public IActionResult Create()
        {
            return View(new Lead());
        }

        // Klienta saglabāšana
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("CompanyName,Website,Email,SourceUrl,SourceType,Description")]
            Lead lead)
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

            TempData["Success"] = "Klients veiksmīgi pievienots!";

            return RedirectToAction(nameof(Index));
        }
    }
}
