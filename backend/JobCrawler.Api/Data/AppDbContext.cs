using JobCrawler.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobCrawler.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<JobPosting> Jobs => Set<JobPosting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<JobPosting>(e =>
        {
            e.HasIndex(j => new { j.Source, j.ExternalId }).IsUnique();
            e.HasIndex(j => j.Category);
            e.HasIndex(j => j.Level);
        });
    }
}
