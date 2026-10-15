using System.Runtime.CompilerServices;
using System.Text.Json;
using JobCrawler.Api.Models;

namespace JobCrawler.Api.Crawling.Sources;

/// <summary>
/// NAB (National Australia Bank) careers on Eightfold — nab.eightfold.ai. Its robots.txt is "Disallow: /" with a short
/// allow-list; the search API /api/pcsx is on it, so that is used (10 jobs per page, filtered to Vietnam).
/// Job pages are /careers/job/&lt;id&gt; (also allowed). It is a bank: credit / financial-crime roles are classified Non-IT.
/// </summary>
public class NabSource(PoliteHttpClient http) : IJobSource
{
    private const string Base = "https://nab.eightfold.ai";
    private const string Company = "NAB Vietnam";
    private const int PageSize = 10;

    public string Name => "nab";

    public async IAsyncEnumerable<RawJob> CrawlAsync(int maxPages, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var page = 0; page < maxPages; page++)
        {
            var json = await http.GetStringAsync(
                $"{Base}/api/pcsx/search?domain=nab.com.au&query=&location=Vietnam&start={page * PageSize}", ct);
            if (json is null) yield break;

            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            var positions = data.GetProperty("positions");
            if (positions.GetArrayLength() == 0) yield break;

            foreach (var p in positions.EnumerateArray())
            {
                var id = p.GetProperty("id").ToString();
                var title = Html.Clean(p.GetProperty("name").GetString());
                if (title is null) continue;

                var locations = p.TryGetProperty("standardizedLocations", out var sl)
                    ? sl.EnumerateArray().Select(l => l.GetString()?.Split(',')[0].Trim()).OfType<string>().Distinct()
                    : [];
                var posted = p.TryGetProperty("postedTs", out var ts) && ts.TryGetInt64(out var secs)
                    ? DateTimeOffset.FromUnixTimeSeconds(secs).ToString("yyyy-MM-dd")
                    : null;

                yield return new RawJob(
                    Source: Name,
                    ExternalId: id,
                    Title: title,
                    Url: $"{Base}/careers/job/{id}",
                    Company: Company,
                    Location: string.Join(", ", locations),
                    SourceCategory: p.TryGetProperty("department", out var dep) ? dep.GetString() : null,
                    Posted: posted);
            }

            var count = data.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
            if ((page + 1) * PageSize >= count) yield break;
        }
    }
}
