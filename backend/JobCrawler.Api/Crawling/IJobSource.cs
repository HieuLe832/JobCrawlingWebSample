using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using JobCrawler.Api.Models;

namespace JobCrawler.Api.Crawling;

public interface IJobSource
{
    /// <summary>Stable key used in the API and DB ("itviec", "topcv", ...).</summary>
    string Name { get; }

    /// <summary>Yields jobs page by page. <paramref name="maxPages"/> bounds the number of listing pages / API pages.</summary>
    IAsyncEnumerable<RawJob> CrawlAsync(int maxPages, CancellationToken ct);
}

internal static partial class Html
{
    private static readonly HtmlParser Parser = new();

    public static IDocument Parse(string html) => Parser.ParseDocument(html);

    /// <summary>Text content with whitespace collapsed; null when empty.</summary>
    public static string? Clean(string? s)
    {
        if (s is null) return null;
        s = WhitespaceRegex().Replace(WebUtility.HtmlDecode(s), " ").Trim();
        return s.Length == 0 ? null : s;
    }

    public static string? Text(this IElement? e) => Clean(e?.TextContent);

    /// <summary>Strips tags from an HTML fragment (for description snippets).</summary>
    public static string? StripTags(string? html, int maxLength = 1500)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var text = Clean(TagRegex().Replace(html, " "));
        return text is { Length: > 0 } && text.Length > maxLength ? text[..maxLength] : text;
    }

    public static string StripQuery(string url)
    {
        var i = url.IndexOfAny(['?', '#']);
        return i < 0 ? url : url[..i];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}
