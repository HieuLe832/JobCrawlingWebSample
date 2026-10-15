using System.Runtime.CompilerServices;
using JobCrawler.Api.Models;

namespace JobCrawler.Api.Crawling.Sources;

/// <summary>
/// netcompany.com — no robots.txt (404 => allowed). /careers/vacancies/ is a Nuxt page that server-renders every
/// vacancy worldwide (~170) in one list; only the Vietnam offices are kept. A single request, so <c>maxPages</c> is ignored.
/// </summary>
public class NetcompanySource(PoliteHttpClient http) : IJobSource
{
    private const string Base = "https://netcompany.com";
    private const string Company = "Netcompany";

    private static readonly string[] VietnamCities = ["Ho Chi Minh", "Hanoi", "Ha Noi", "Da Nang", "Vietnam", "Viet Nam"];

    public string Name => "netcompany";

    public async IAsyncEnumerable<RawJob> CrawlAsync(int maxPages, [EnumeratorCancellation] CancellationToken ct)
    {
        var html = await http.GetStringAsync($"{Base}/careers/vacancies/", ct);
        if (html is null) yield break;

        var seen = new HashSet<string>();
        foreach (var link in Html.Parse(html).QuerySelectorAll("a[href^='/job/']"))
        {
            var city = link.QuerySelector("p.job-city").Text();
            if (city is null || !VietnamCities.Any(c => city.Contains(c, StringComparison.OrdinalIgnoreCase))) continue;

            var href = Html.StripQuery(link.GetAttribute("href")!);
            // e.g. /job/REF510F-Ho-Chi-Minh-City/ — the same reference can appear in several lists on the page.
            var id = href.Trim('/').Split('/').Last();
            var title = link.QuerySelector("h6").Text();
            if (title is null || !seen.Add(id)) continue;

            yield return new RawJob(
                Source: Name,
                ExternalId: id,
                Title: title,
                Url: Base + href,
                Company: Company,
                Location: city);
        }
    }
}
