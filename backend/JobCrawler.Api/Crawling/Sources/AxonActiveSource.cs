using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JobCrawler.Api.Models;

namespace JobCrawler.Api.Crawling.Sources;

/// <summary>
/// careers.axonactive.com — a Wix site where each job is a blog post (/post/&lt;slug&gt;).
/// The blog RSS feed (blog-feed.xml) lists them all with title, link, date, description, and the
/// post categories are the office locations. A single request, so <c>maxPages</c> is ignored.
/// </summary>
public partial class AxonActiveSource(PoliteHttpClient http) : IJobSource
{
    private const string FeedUrl = "https://www.careers.axonactive.com/blog-feed.xml";
    private const string Company = "Axon Active";

    public string Name => "axonactive";

    public async IAsyncEnumerable<RawJob> CrawlAsync(int maxPages, [EnumeratorCancellation] CancellationToken ct)
    {
        var xml = await http.GetStringAsync(FeedUrl, ct);
        if (xml is null) yield break;

        foreach (var item in XDocument.Parse(xml).Descendants("item"))
        {
            var link = item.Element("link")?.Value.Trim();
            // Titles start with the head count: "01 Fullstack Developer", "02 Senior Kafka Platform Engineers".
            var title = Html.Clean(HeadCountRegex().Replace(item.Element("title")?.Value ?? "", ""));
            if (link is null || title is null) continue;

            yield return new RawJob(
                Source: Name,
                ExternalId: Html.StripQuery(link).TrimEnd('/').Split('/').Last(),
                Title: title,
                Url: link,
                Company: Company,
                Location: string.Join(", ", item.Elements("category").Select(c => c.Value.Trim())),
                Posted: DateTimeOffset.TryParse(item.Element("pubDate")?.Value, out var d) ? d.ToString("yyyy-MM-dd") : null,
                Snippet: Html.StripTags(item.Element("description")?.Value));
        }
    }

    [GeneratedRegex(@"^\s*\d{1,3}\s+")]
    private static partial Regex HeadCountRegex();
}
