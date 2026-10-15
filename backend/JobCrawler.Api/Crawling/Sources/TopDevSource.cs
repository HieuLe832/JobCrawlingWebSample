using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using JobCrawler.Api.Models;

namespace JobCrawler.Api.Crawling.Sources;

/// <summary>
/// topdev.vn — the search page is a Next.js app whose results load client-side from api.topdev.vn,
/// and api.topdev.vn/robots.txt is "Disallow: /". So instead:
///   sitemap-jobs.xml (declared in robots.txt) -> jobs_desc_vi_page_N.xml -> each job page -> JSON-LD JobPosting.
/// One "page" = one sitemap page (~20 jobs, one request each). Sitemaps still list expired jobs (404, skipped),
/// and TopDev now shares its job pool with Saramin, so many jobs are not IT: the classifier filters them.
/// The JSON-LD "industry" field is always "Information Technology" and is ignored.
/// </summary>
public class TopDevSource(PoliteHttpClient http, ILogger<TopDevSource> logger) : IJobSource
{
    private const string SitemapIndex = "https://topdev.vn/sitemap-jobs.xml";

    public string Name => "topdev";

    public async IAsyncEnumerable<RawJob> CrawlAsync(int maxPages, [EnumeratorCancellation] CancellationToken ct)
    {
        var indexXml = await http.GetStringAsync(SitemapIndex, ct);
        if (indexXml is null) yield break;

        var sitemapPages = Locs(indexXml).Where(u => u.Contains("jobs_desc_vi_page_")).Take(maxPages).ToList();

        foreach (var sitemapUrl in sitemapPages)
        {
            var pageXml = await http.GetStringAsync(sitemapUrl, ct);
            if (pageXml is null) continue;

            foreach (var jobUrl in Locs(pageXml))
            {
                RawJob? job = null;
                try
                {
                    var html = await http.GetStringAsync(jobUrl, ct);
                    if (html is not null) job = ParseJobPage(html, jobUrl);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "TopDev job page failed: {Url}", jobUrl);
                }
                if (job is not null) yield return job;
            }
        }
    }

    private static IEnumerable<string> Locs(string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value.Trim());
    }

    private RawJob? ParseJobPage(string html, string url)
    {
        var doc = Html.Parse(html);
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            JsonDocument json;
            try { json = JsonDocument.Parse(script.TextContent); }
            catch (JsonException) { continue; }

            using (json)
            {
                var root = json.RootElement;
                if (Str(root, "@type") != "JobPosting") continue;

                var title = Html.Clean(Str(root, "title"));
                if (title is null) return null;

                // Job id is the trailing number of the slug: ...-globaltech-2136294
                var slug = Html.StripQuery(url).TrimEnd('/').Split('/').Last();
                var id = slug[(slug.LastIndexOf('-') + 1)..];

                var months = root.TryGetProperty("experienceRequirements", out var exp) && exp.ValueKind == JsonValueKind.Object
                    && exp.TryGetProperty("monthsOfExperience", out var m) && m.TryGetInt32(out var mv) ? mv : (int?)null;

                return new RawJob(
                    Source: Name,
                    ExternalId: id,
                    Title: title,
                    Url: Html.StripQuery(url),
                    Company: root.TryGetProperty("hiringOrganization", out var org) ? Html.Clean(Str(org, "name")) : null,
                    Location: Location(root),
                    Salary: Salary(root),
                    Skills: (Str(root, "skills") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                    SourceLevel: months is null ? null : $"{months / 12.0:0.#} năm kinh nghiệm",
                    Posted: Str(root, "datePosted"),
                    Snippet: Html.StripTags(Str(root, "description")));
            }
        }
        return null;
    }

    private static string? Location(JsonElement root)
    {
        if (!root.TryGetProperty("jobLocation", out var loc)) return null;
        var places = loc.ValueKind == JsonValueKind.Array ? loc.EnumerateArray().ToList() : [loc];
        var regions = places
            .Select(p => p.TryGetProperty("address", out var a) ? Str(a, "addressRegion") : null)
            .OfType<string>()
            .Distinct();
        return Html.Clean(string.Join(", ", regions));
    }

    private static string? Salary(JsonElement root)
    {
        if (!root.TryGetProperty("baseSalary", out var s) || !s.TryGetProperty("value", out var v)) return null;
        if (v.ValueKind != JsonValueKind.Object) return Html.Clean(v.ToString());
        var currency = Str(s, "currency");
        if (v.TryGetProperty("minValue", out var min) && v.TryGetProperty("maxValue", out var max))
            return $"{min} - {max} {currency}";
        var value = v.TryGetProperty("value", out var single) ? single.ToString() : null;
        return value == "Negotiable" ? "Thoả thuận" : Html.Clean(value);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
