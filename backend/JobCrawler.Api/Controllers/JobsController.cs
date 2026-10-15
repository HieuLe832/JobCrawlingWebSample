using JobCrawler.Api.Classification;
using JobCrawler.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobCrawler.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobsController(AppDbContext db) : ControllerBase
{
    /// <summary>List jobs with filters. By default only IT jobs (includeNonIt=false).</summary>
    [HttpGet]
    public async Task<object> List(
        string? source, string? category, string? level, string? q,
        bool includeNonIt = false, int page = 1, int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var query = db.Jobs.AsNoTracking().AsQueryable();
        if (!includeNonIt) query = query.Where(j => j.IsIt);
        if (!string.IsNullOrEmpty(source)) query = query.Where(j => j.Source == source);
        if (!string.IsNullOrEmpty(category)) query = query.Where(j => j.Category == category);
        if (!string.IsNullOrEmpty(level)) query = query.Where(j => j.Level == level);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(j => EF.Functions.Like(j.Title, like) || EF.Functions.Like(j.Company!, like)
                                     || EF.Functions.Like(j.Skills!, like));
        }

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(j => j.LastSeenAt).ThenByDescending(j => j.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, pageSize, items };
    }

    /// <summary>Counts per category / level / source — the "classification" view.</summary>
    [HttpGet("stats")]
    public async Task<object> Stats(bool includeNonIt = false)
    {
        var query = db.Jobs.AsNoTracking();
        if (!includeNonIt) query = query.Where(j => j.IsIt);

        return new
        {
            total = await query.CountAsync(),
            nonIt = await db.Jobs.CountAsync(j => !j.IsIt),
            byCategory = await query.GroupBy(j => j.Category).Select(g => new { key = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count).ToListAsync(),
            byLevel = await query.GroupBy(j => j.Level).Select(g => new { key = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count).ToListAsync(),
            bySource = await query.GroupBy(j => j.Source).Select(g => new { key = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count).ToListAsync(),
        };
    }

    /// <summary>Re-run the classifier over every stored job (after changing rules, no re-crawl needed).</summary>
    [HttpPost("reclassify")]
    public async Task<object> Reclassify()
    {
        var jobs = await db.Jobs.ToListAsync();
        var changed = 0;
        foreach (var job in jobs)
        {
            var c = JobClassifier.Classify(job);
            if (job.IsIt != c.IsIt || job.Category != c.Category || job.Level != c.Level) changed++;
            (job.IsIt, job.Category, job.Level, job.CategoryScore) = (c.IsIt, c.Category, c.Level, c.Score);
        }
        await db.SaveChangesAsync();
        return new { total = jobs.Count, changed };
    }
}
