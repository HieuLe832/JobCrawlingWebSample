using JobCrawler.Api.Classification;
using JobCrawler.Api.Data;
using JobCrawler.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobCrawler.Api.Crawling;

public class SourceStatus
{
    public string Source { get; init; } = "";
    public string State { get; set; } = "idle"; // idle | running | done | failed
    public int Fetched { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public string? Error { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

/// <summary>
/// Singleton that runs one crawl at a time in the background (crawls take minutes, longer than an HTTP request should).
/// Sources run in parallel with each other; requests to one host stay sequential and throttled in <see cref="PoliteHttpClient"/>.
/// </summary>
public class CrawlCoordinator(IEnumerable<IJobSource> sources, IServiceScopeFactory scopes, ILogger<CrawlCoordinator> logger)
{
    private readonly Dictionary<string, IJobSource> _sources = sources.ToDictionary(s => s.Name);
    private readonly Dictionary<string, SourceStatus> _status = sources.ToDictionary(s => s.Name, s => new SourceStatus { Source = s.Name });
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public IReadOnlyCollection<string> SourceNames => _sources.Keys;

    public bool IsRunning { get { lock (_lock) return _cts != null; } }

    public IReadOnlyList<SourceStatus> Status { get { lock (_lock) return _status.Values.ToList(); } }

    /// <returns>false if a crawl is already running.</returns>
    public bool Start(IEnumerable<string> names, int maxPages)
    {
        var selected = names.Where(_sources.ContainsKey).Distinct().ToList();
        lock (_lock)
        {
            if (_cts != null) return false;
            _cts = new CancellationTokenSource();
            foreach (var n in selected)
                _status[n] = new SourceStatus { Source = n, State = "running", StartedAt = DateTime.UtcNow };
        }

        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.WhenAll(selected.Select(n => RunSourceAsync(_sources[n], maxPages, ct))); }
            finally { lock (_lock) { _cts.Dispose(); _cts = null; } }
        });
        return true;
    }

    public void Cancel()
    {
        lock (_lock) _cts?.Cancel();
    }

    private async Task RunSourceAsync(IJobSource source, int maxPages, CancellationToken ct)
    {
        var status = _status[source.Name];
        try
        {
            var batch = new List<RawJob>();
            await foreach (var job in source.CrawlAsync(maxPages, ct))
            {
                batch.Add(job);
                lock (_lock) status.Fetched++;
                if (batch.Count >= 20) { await SaveAsync(batch, status, ct); batch.Clear(); }
            }
            await SaveAsync(batch, status, ct);
            lock (_lock) status.State = "done";
        }
        catch (OperationCanceledException)
        {
            lock (_lock) { status.State = "failed"; status.Error = "cancelled"; }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Crawl of {Source} failed", source.Name);
            lock (_lock) { status.State = "failed"; status.Error = ex.Message; }
        }
        finally
        {
            lock (_lock) status.FinishedAt = DateTime.UtcNow;
        }
    }

    /// <summary>Upsert by (Source, ExternalId) and classify. Saved per batch so progress survives a failure midway.</summary>
    private async Task SaveAsync(List<RawJob> batch, SourceStatus status, CancellationToken ct)
    {
        if (batch.Count == 0) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A listing can show the same job twice (e.g. pinned + normal); keep the last one.
        var unique = batch.GroupBy(r => r.ExternalId).Select(g => g.Last()).ToList();
        var source = unique[0].Source;
        var ids = unique.Select(r => r.ExternalId).ToList();
        var existing = await db.Jobs.Where(j => j.Source == source && ids.Contains(j.ExternalId))
            .ToDictionaryAsync(j => j.ExternalId, ct);

        var now = DateTime.UtcNow;
        int inserted = 0, updated = 0;
        foreach (var raw in unique)
        {
            if (!existing.TryGetValue(raw.ExternalId, out var job))
            {
                job = new JobPosting { Source = raw.Source, ExternalId = raw.ExternalId, FirstSeenAt = now };
                db.Jobs.Add(job);
                inserted++;
            }
            else updated++;

            job.Title = raw.Title;
            job.Url = raw.Url;
            job.Company = raw.Company;
            job.Location = raw.Location;
            job.Salary = raw.Salary;
            job.Skills = raw.Skills is { Count: > 0 } ? string.Join(", ", raw.Skills) : null;
            job.SourceCategory = string.IsNullOrWhiteSpace(raw.SourceCategory) ? null : raw.SourceCategory;
            job.SourceLevel = raw.SourceLevel;
            job.Posted = raw.Posted;
            job.Snippet = raw.Snippet;
            job.LastSeenAt = now;

            var c = JobClassifier.Classify(job);
            (job.IsIt, job.Category, job.Level, job.CategoryScore) = (c.IsIt, c.Category, c.Level, c.Score);
        }

        await db.SaveChangesAsync(ct);
        lock (_lock) { status.Inserted += inserted; status.Updated += updated; }
    }
}
