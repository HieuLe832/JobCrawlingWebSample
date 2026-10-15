using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace JobCrawler.Api.Crawling;

public class RobotsDisallowedException(string url) : Exception($"robots.txt disallows {url}");

/// <summary>
/// The only way crawlers talk to the network. Every request:
///  1. is checked against the host's robots.txt (fetched once and cached),
///  2. waits so requests to the same host are at least <see cref="CrawlerOptions.DelayMs"/> apart,
///  3. is retried on 429/5xx with backoff.
/// </summary>
public class PoliteHttpClient(HttpClient http, CrawlerOptions options, ILogger<PoliteHttpClient> logger)
{
    private readonly ConcurrentDictionary<string, Task<RobotsTxt>> _robots = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostLocks = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastRequest = new();

    public Task<string?> GetStringAsync(string url, CancellationToken ct) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);

    public Task<string?> PostJsonAsync(string url, string json, IDictionary<string, string>? headers, CancellationToken ct) =>
        SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (headers != null)
                foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
            return req;
        }, ct);

    public async Task<bool> IsAllowedAsync(Uri uri, CancellationToken ct)
    {
        var robots = await _robots.GetOrAdd(uri.GetLeftPart(UriPartial.Authority), FetchRobotsAsync);
        return robots.IsAllowed(uri.PathAndQuery);
    }

    /// <returns>Response body, or null on 404/410 (gone job pages are normal).</returns>
    private async Task<string?> SendAsync(Func<HttpRequestMessage> build, CancellationToken ct)
    {
        var probe = build();
        var uri = probe.RequestUri!;
        if (!await IsAllowedAsync(uri, ct)) throw new RobotsDisallowedException(uri.ToString());

        for (var attempt = 1; ; attempt++)
        {
            await ThrottleAsync(uri.Host, ct);
            using var req = attempt == 1 ? probe : build();
            using var res = await http.SendAsync(req, ct);

            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return null;
            if (res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync(ct);
            if (IsBotChallenge(res))
                throw new HttpRequestException($"{uri.Host} answered with a bot challenge; not crawling it", null, res.StatusCode);

            var retryable = res.StatusCode == HttpStatusCode.TooManyRequests || (int)res.StatusCode >= 500;
            if (!retryable || attempt >= 3)
                throw new HttpRequestException($"{(int)res.StatusCode} {res.ReasonPhrase} for {uri}", null, res.StatusCode);

            var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * attempt);
            logger.LogWarning("{Status} from {Uri}, retrying in {Wait}", (int)res.StatusCode, uri, wait);
            await Task.Delay(wait, ct);
        }
    }

    private static bool IsBotChallenge(HttpResponseMessage res) =>
        res.Headers.TryGetValues("cf-mitigated", out var v) && v.Contains("challenge");

    private async Task ThrottleAsync(string host, CancellationToken ct)
    {
        var gate = _hostLocks.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_lastRequest.TryGetValue(host, out var last))
            {
                var wait = last.AddMilliseconds(options.DelayMs) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            }
            _lastRequest[host] = DateTime.UtcNow;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<RobotsTxt> FetchRobotsAsync(string origin)
    {
        try
        {
            using var res = await http.GetAsync(origin + "/robots.txt");
            // A bot challenge (Cloudflare "Just a moment...") is not the site's robots answer: the site is refusing bots.
            if (IsBotChallenge(res))
            {
                logger.LogWarning("robots.txt for {Origin} is behind a bot challenge -> disallow all", origin);
                return RobotsTxt.DisallowAll;
            }
            // RFC 9309: 4xx => no restrictions, 5xx/unreachable => assume full disallow.
            if ((int)res.StatusCode is >= 400 and < 500)
            {
                logger.LogInformation("robots.txt for {Origin}: {Status} -> allow all", origin, (int)res.StatusCode);
                return RobotsTxt.AllowAll;
            }
            if (!res.IsSuccessStatusCode) return RobotsTxt.DisallowAll;
            return RobotsTxt.Parse(await res.Content.ReadAsStringAsync(), options.UserAgentToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch robots.txt for {Origin}; treating as disallow-all", origin);
            _robots.TryRemove(origin, out _); // try again next run
            return RobotsTxt.DisallowAll;
        }
    }
}

public class CrawlerOptions
{
    /// <summary>Product token matched against robots.txt "User-agent" lines.</summary>
    public string UserAgentToken { get; set; } = "JobCrawlerBot";
    public string UserAgent { get; set; } = "Mozilla/5.0 (compatible; JobCrawlerBot/0.1; +https://example.com/bot)";
    public int DelayMs { get; set; } = 1500;
    public int TimeoutSeconds { get; set; } = 30;
}
