using JobCrawler.Api.Crawling;
using Microsoft.AspNetCore.Mvc;

namespace JobCrawler.Api.Controllers;

public record CrawlRequest(string[]? Sources, int MaxPages = 2);

[ApiController]
[Route("api/crawl")]
public class CrawlController(CrawlCoordinator coordinator) : ControllerBase
{
    /// <summary>Start a background crawl. Returns 409 if one is already running.</summary>
    [HttpPost]
    public IActionResult Start([FromBody] CrawlRequest req)
    {
        var sources = req.Sources is { Length: > 0 } ? req.Sources : coordinator.SourceNames.ToArray();
        var unknown = sources.Except(coordinator.SourceNames).ToArray();
        if (unknown.Length > 0) return BadRequest(new { error = $"Unknown source(s): {string.Join(", ", unknown)}" });
        if (req.MaxPages is < 1 or > 50) return BadRequest(new { error = "maxPages must be 1..50" });

        if (!coordinator.Start(sources, req.MaxPages)) return Conflict(new { error = "A crawl is already running" });
        return Accepted(Status());
    }

    [HttpGet("status")]
    public object Status() => new { running = coordinator.IsRunning, sources = coordinator.Status };

    [HttpPost("cancel")]
    public IActionResult Cancel()
    {
        coordinator.Cancel();
        return Ok(Status());
    }
}
