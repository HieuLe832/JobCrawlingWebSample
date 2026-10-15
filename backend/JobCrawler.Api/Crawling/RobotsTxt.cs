using System.Text.RegularExpressions;

namespace JobCrawler.Api.Crawling;

/// <summary>
/// Minimal RFC 9309 robots.txt matcher:
/// - picks the group(s) for our user-agent token, else "*" (multiple groups for the same agent are merged),
/// - supports "*" and "$" wildcards,
/// - longest matching rule wins; on a tie Allow wins.
/// </summary>
public class RobotsTxt
{
    private readonly List<(bool Allow, string Pattern, Regex Regex)> _rules;

    public static RobotsTxt AllowAll { get; } = new([]);
    public static RobotsTxt DisallowAll { get; } = new([(false, "/", ToRegex("/"))]);

    private RobotsTxt(List<(bool, string, Regex)> rules) => _rules = rules;

    public static RobotsTxt Parse(string content, string userAgentToken)
    {
        var token = userAgentToken.ToLowerInvariant();
        var specific = new List<(bool, string, Regex)>();
        var wildcard = new List<(bool, string, Regex)>();

        var currentAgents = new List<string>();
        var lastWasAgent = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            var colon = line.IndexOf(':');
            if (colon < 0) continue;

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            if (key == "user-agent")
            {
                if (!lastWasAgent) currentAgents = [];
                currentAgents.Add(value.ToLowerInvariant());
                lastWasAgent = true;
                continue;
            }

            lastWasAgent = false;
            if (key is not ("allow" or "disallow")) continue;
            // "Disallow:" with an empty value means allow everything — it adds no rule.
            if (value.Length == 0) continue;

            var rule = (key == "allow", value, ToRegex(value));
            foreach (var agent in currentAgents)
            {
                if (agent == "*") wildcard.Add(rule);
                else if (token.Contains(agent)) specific.Add(rule);
            }
        }

        return new RobotsTxt(specific.Count > 0 ? specific : wildcard);
    }

    public bool IsAllowed(string pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery)) pathAndQuery = "/";
        (bool Allow, int Length)? best = null;

        foreach (var (allow, pattern, regex) in _rules)
        {
            if (!regex.IsMatch(pathAndQuery)) continue;
            var len = pattern.Length;
            if (best is null || len > best.Value.Length || (len == best.Value.Length && allow))
                best = (allow, len);
        }

        return best?.Allow ?? true;
    }

    private static Regex ToRegex(string pattern)
    {
        var anchoredEnd = pattern.EndsWith('$');
        if (anchoredEnd) pattern = pattern[..^1];
        var re = "^" + Regex.Escape(pattern).Replace(@"\*", ".*") + (anchoredEnd ? "$" : "");
        return new Regex(re, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
