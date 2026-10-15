using System.Runtime.CompilerServices;
using JobCrawler.Api.Models;

namespace JobCrawler.Api.Crawling.Sources;

/// <summary>
/// itviec.com — server-rendered listing (Rails + Stimulus), 20 jobs per page at /it-jobs?page=N.
/// Every job on ITViec is an IT job. Salary requires login, so it is left empty.
/// </summary>
public class ItViecSource(PoliteHttpClient http) : IJobSource
{
    private const string Base = "https://itviec.com";

    public string Name => "itviec";

    public async IAsyncEnumerable<RawJob> CrawlAsync(int maxPages, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var page = 1; page <= maxPages; page++)
        {
            var html = await http.GetStringAsync($"{Base}/it-jobs?page={page}", ct);
            if (html is null) yield break;

            var cards = Html.Parse(html).QuerySelectorAll("div.job-card[data-search--job-selection-job-slug-value]");
            if (cards.Length == 0) yield break;

            foreach (var card in cards)
            {
                var slug = card.GetAttribute("data-search--job-selection-job-slug-value")!;
                var title = card.QuerySelector("h3 a").Text() ?? card.QuerySelector("h3").Text();
                if (title is null) continue;

                var skills = card.QuerySelectorAll("a.itag").Select(a => a.Text()).OfType<string>().ToList();
                // The job "role" link (e.g. "AI / Machine Learning Engineer") points at /it-jobs/<role> and carries a title attribute.
                var role = card.QuerySelector("a.text-decoration-dot-underline[title]")?.GetAttribute("title");
                var location = card.QuerySelector("div.text-truncate[title]")?.GetAttribute("title");
                var posted = card.QuerySelector("span.small-text.text-dark-grey").Text();

                yield return new RawJob(
                    Source: Name,
                    ExternalId: slug,
                    Title: title,
                    Url: $"{Base}/it-jobs/{slug}",
                    Company: card.QuerySelector("span a[href^='/companies/']").Text(),
                    Location: Html.Clean(location),
                    Skills: skills,
                    SourceCategory: Html.Clean(role),
                    Posted: posted);
            }
        }
    }
}
