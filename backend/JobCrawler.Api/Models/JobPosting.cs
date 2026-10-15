namespace JobCrawler.Api.Models;

/// <summary>A job stored in the database (one row per Source + ExternalId).</summary>
public class JobPosting
{
    public int Id { get; set; }
    public string Source { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Company { get; set; }
    public string? Location { get; set; }
    public string? Salary { get; set; }
    /// <summary>Comma-separated skill tags as given by the site.</summary>
    public string? Skills { get; set; }
    /// <summary>The site's own category / job function, used as a hint by the classifier.</summary>
    public string? SourceCategory { get; set; }
    public string? SourceLevel { get; set; }
    public string? Posted { get; set; }
    public string Url { get; set; } = "";
    /// <summary>Plain-text snippet of the description (truncated) — only some sources provide it.</summary>
    public string? Snippet { get; set; }

    // Classification output
    public bool IsIt { get; set; }
    public string Category { get; set; } = "";
    public string Level { get; set; } = "";
    public int CategoryScore { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}

/// <summary>What a source crawler returns, before classification and persistence.</summary>
public record RawJob(
    string Source,
    string ExternalId,
    string Title,
    string Url,
    string? Company = null,
    string? Location = null,
    string? Salary = null,
    IReadOnlyList<string>? Skills = null,
    string? SourceCategory = null,
    string? SourceLevel = null,
    string? Posted = null,
    string? Snippet = null);
