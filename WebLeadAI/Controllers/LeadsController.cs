
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebLeadAI.Data;

namespace WebLeadAI.Controllers
{
    public class LeadsController : Controller
    {
        private readonly AppDbContext _context;

        public LeadsController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var leads = await _context.Leads
                .AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return View(leads);
        }
    }
}
