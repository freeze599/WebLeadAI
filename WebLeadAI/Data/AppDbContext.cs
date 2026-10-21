
using Microsoft.EntityFrameworkCore;
using WebLeadAI.Models;

namespace WebLeadAI.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(
            DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Lead> Leads => Set<Lead>();
    }
}
