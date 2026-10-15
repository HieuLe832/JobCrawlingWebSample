using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobCrawler.Api.Models;

namespace JobCrawler.Api.Crawling.Sources;

/// <summary>
/// vietnamworks.com — search results are not server-rendered; the site's own frontend POSTs to
/// ms.vietnamworks.com/job-search/v1.0/search. That host has no robots.txt (404 => no restrictions, RFC 9309).
/// Filtered by job function parentId 5 = "Công Nghệ Thông Tin/Viễn Thông". 50 jobs per API page.
/// </summary>
public class VietnamWorksSource(PoliteHttpClient http) : IJobSource
{
    private const string SearchUrl = "https://ms.vietnamworks.com/job-search/v1.0/search";
    private const int ItJobFunctionId = 5;
    private const int PageSize = 50;

    private static readonly string[] Fields =
    [
        "jobId", "jobTitle", "alias", "companyName", "workingLocations", "skills", "jobLevelVI",
        "prettySalary", "approvedOn", "jobFunction", "jobRequirement",
    ];

    private static readonly Dictionary<string, string> Headers = new()
    {
        ["Accept-Language"] = "vi",
        ["X-Source"] = "Page-Container",
    };

    public string Name => "vietnamworks";

    public async IAsyncEnumerable<RawJob> CrawlAsync(int maxPages, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var page = 0; page < maxPages; page++)
        {
            var body = new JsonObject
            {
                ["userId"] = 0,
                ["query"] = "",
                // The filter value is itself a JSON string — that is how the site's frontend sends it.
                ["filter"] = new JsonArray(new JsonObject
                {
                    ["field"] = "jobFunction",
                    ["value"] = $"[{{\"parentId\":{ItJobFunctionId},\"childrenIds\":[-1]}}]",
                }),
                ["ranges"] = new JsonArray(),
                ["order"] = new JsonArray(new JsonObject { ["field"] = "approvedOn", ["value"] = "desc" }),
                ["hitsPerPage"] = PageSize,
                ["page"] = page,
                ["retrieveFields"] = new JsonArray(Fields.Select(f => (JsonNode)f!).ToArray()),
            };

            var json = await http.PostJsonAsync(SearchUrl, body.ToJsonString(), Headers, ct);
            if (json is null) yield break;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) yield break;

            foreach (var j in data.EnumerateArray())
            {
                var id = j.GetProperty("jobId").ToString();
                var alias = j.TryGetProperty("alias", out var a) ? a.GetString() : null;
                var title = Html.Clean(j.GetProperty("jobTitle").GetString());
                if (title is null) continue;

                var cities = j.TryGetProperty("workingLocations", out var wl) && wl.ValueKind == JsonValueKind.Array
                    ? wl.EnumerateArray().Select(l => l.TryGetProperty("cityNameVI", out var c) ? c.GetString() : null)
                        .OfType<string>().Distinct()
                    : [];

                var skills = j.TryGetProperty("skills", out var sk) && sk.ValueKind == JsonValueKind.Array
                    ? sk.EnumerateArray().Select(s => s.GetProperty("skillName").GetString()).OfType<string>().ToList()
                    : [];

                // Sub-function names, e.g. "Software Engineering", "Business/System Analysis".
                var functions = j.TryGetProperty("jobFunction", out var jf) && jf.TryGetProperty("children", out var ch)
                                && ch.ValueKind == JsonValueKind.Array
                    ? ch.EnumerateArray().Select(c => c.GetProperty("name").GetString()).OfType<string>()
                    : [];

                yield return new RawJob(
                    Source: Name,
                    ExternalId: id,
                    Title: title,
                    Url: $"https://www.vietnamworks.com/{alias}-{id}-jd",
                    Company: Html.Clean(j.TryGetProperty("companyName", out var cn) ? cn.GetString() : null),
                    Location: string.Join(", ", cities),
                    Salary: j.TryGetProperty("prettySalary", out var ps) ? ps.GetString() : null,
                    Skills: skills,
                    SourceCategory: string.Join(", ", functions),
                    SourceLevel: j.TryGetProperty("jobLevelVI", out var lv) ? lv.GetString() : null,
                    Posted: j.TryGetProperty("approvedOn", out var ap) ? ap.GetString() : null,
                    Snippet: Html.StripTags(j.TryGetProperty("jobRequirement", out var rq) ? rq.GetString() : null));
            }

            var nbPages = doc.RootElement.GetProperty("meta").TryGetProperty("nbPages", out var np) ? np.GetInt32() : 0;
            if (page + 1 >= nbPages) yield break;
        }
    }
}
