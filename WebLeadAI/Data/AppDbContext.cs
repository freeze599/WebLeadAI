
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
        public DbSet<LeadEmailOutbox> LeadEmailOutbox => Set<LeadEmailOutbox>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<LeadEmailOutbox>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.SourceUrl).IsRequired().HasMaxLength(500);
                entity.Property(x => x.Title).IsRequired().HasMaxLength(200);
                entity.HasIndex(x => x.SourceUrl).IsUnique();
                entity.HasIndex(x => new { x.SentAtUtc, x.NextAttemptUtc });
            });
        }
    }
}
