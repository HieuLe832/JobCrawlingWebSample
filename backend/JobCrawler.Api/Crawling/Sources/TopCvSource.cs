using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using JobCrawler.Api.Models;

namespace JobCrawler.Api.Crawling.Sources;

/// <summary>
/// topcv.vn — server-rendered listing, ~50 jobs per page.
/// "cong-nghe-thong-tin-cr257" is the whole IT category (all IT sub-categories).
/// Old category URLs (e.g. ...-c10026) 301-redirect here, so the canonical URL is used directly.
/// </summary>
public partial class TopCvSource(PoliteHttpClient http) : IJobSource
{
    private const string ListingUrl = "https://www.topcv.vn/tim-viec-lam-cong-nghe-thong-tin-cr257";

    public string Name => "topcv";

    public async IAsyncEnumerable<RawJob> CrawlAsync(int maxPages, [EnumeratorCancellation] CancellationToken ct)
    {
        var seen = new HashSet<string>();
        for (var page = 1; page <= maxPages; page++)
        {
            var html = await http.GetStringAsync($"{ListingUrl}?page={page}", ct);
            if (html is null) yield break;

            var items = Html.Parse(html).QuerySelectorAll("div.job-item-search-result[data-job-id]");
            if (items.Length == 0) yield break;

            var newOnPage = 0;
            foreach (var item in items)
            {
                var id = item.GetAttribute("data-job-id")!;
                if (!seen.Add(id)) continue;
                newOnPage++;

                var link = item.QuerySelector("h3.title a");
                var title = link?.QuerySelector("span[title]")?.GetAttribute("title") ?? link.Text();
                var href = link?.GetAttribute("href");
                if (title is null || href is null) continue;

                // Visible tags are truncated ("Mobile Devel..."); the full list lives in the tooltip of ".remaining-items".
                var tags = item.QuerySelectorAll(".box-icon .tag span.item-tag").Select(t => t.Text()).OfType<string>()
                    .Where(t => !t.EndsWith("..."))
                    .Concat((item.QuerySelector(".box-icon .tag .remaining-items")?.GetAttribute("title") ?? "")
                        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    .Where(t => !NoiseTagRegex().IsMatch(t))
                    .Distinct()
                    .ToList();

                var experience = tags.FirstOrDefault(t => ExperienceRegex().IsMatch(t));

                yield return new RawJob(
                    Source: Name,
                    ExternalId: id,
                    Title: Html.Clean(title)!,
                    Url: Html.StripQuery(href),
                    Company: item.QuerySelector("a.company span.company-name").Text(),
                    Location: item.QuerySelector("span.city-text").Text(),
                    Salary: item.QuerySelector("label.title-salary").Text(),
                    Skills: tags.Where(t => t != experience).ToList(),
                    SourceLevel: experience,
                    Posted: item.QuerySelector("label.label-update").Text());
            }

            // Guard against a page past the end that repeats earlier results instead of returning 404.
            if (newOnPage == 0) yield break;
        }
    }

    /// <summary>Education and gender requirements are mixed into the same tag list.</summary>
    [GeneratedRegex(@"trở lên|^(Nam|Nữ)$|^Nam/Nữ$", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseTagRegex();

    [GeneratedRegex(@"kinh nghiệm|năm|tháng", RegexOptions.IgnoreCase)]
    private static partial Regex ExperienceRegex();
}
