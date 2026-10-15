using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using JobCrawler.Api.Models;

namespace JobCrawler.Api.Crawling.Sources;

/// <summary>
/// tma.vn — TMA Solutions' own careers page, server-rendered, ~9 jobs per page at /tuyen-dung/viec-lam?page=N.
/// CSS class names are hashed per build ("_1fB0q_item"), so selectors match on the stable suffix only.
/// Also lists marketing / business development jobs: the classifier filters them.
/// A page past the last one answers 500 instead of an empty list, so the last page is read from the pager links.
/// </summary>
public partial class TmaSource(PoliteHttpClient http) : IJobSource
{
    private const string Base = "https://www.tma.vn";
    private const string Company = "TMA Solutions";

    private static readonly string[] Cities = ["Hồ Chí Minh", "Đà Nẵng", "Hà Nội", "Cần Thơ", "Bình Định", "Quy Nhơn"];

    public string Name => "tma";

    public async IAsyncEnumerable<RawJob> CrawlAsync(int maxPages, [EnumeratorCancellation] CancellationToken ct)
    {
        var seen = new HashSet<string>();
        var lastPage = maxPages;
        for (var page = 1; page <= Math.Min(maxPages, lastPage); page++)
        {
            var html = await http.GetStringAsync($"{Base}/tuyen-dung/viec-lam?page={page}", ct);
            if (html is null) yield break;

            var doc = Html.Parse(html);
            var pagerMax = doc.QuerySelectorAll("a[href*='viec-lam?page=']")
                .Select(a => PageRegex().Match(a.GetAttribute("href") ?? ""))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value))
                .DefaultIfEmpty(page)
                .Max();
            lastPage = Math.Max(page, pagerMax);

            var links = doc.QuerySelectorAll("div[class$='_job_title'] a[href*='/tuyen-dung/chi-tiet/']");
            if (links.Length == 0) yield break;

            foreach (var link in links)
            {
                var href = link.GetAttribute("href")!;
                // Slug ends with the job id: /tuyen-dung/chi-tiet/ai-engineer-1073
                var slug = Html.StripQuery(href).TrimEnd('/').Split('/').Last();
                var id = slug[(slug.LastIndexOf('-') + 1)..];
                if (!seen.Add(id)) continue;

                var title = link.Text();
                if (title is null) continue;

                var item = link.Closest("div[class$='_item']");
                var address = item?.QuerySelector("[class$='_job_location']").Text();
                var expires = item?.QuerySelector("[class$='_expired_date']").Text();

                yield return new RawJob(
                    Source: Name,
                    ExternalId: id,
                    Title: title,
                    Url: Base + Html.StripQuery(href),
                    Company: Company,
                    Location: CityOf(address),
                    Posted: expires);
            }
        }
    }

    [GeneratedRegex(@"[?&]page=(\d+)")]
    private static partial Regex PageRegex();

    /// <summary>Addresses are full street addresses; keep just the city like the other sources.</summary>
    private static string? CityOf(string? address) =>
        address is null ? null : Cities.FirstOrDefault(c => address.Contains(c, StringComparison.OrdinalIgnoreCase)) ?? address;
}
