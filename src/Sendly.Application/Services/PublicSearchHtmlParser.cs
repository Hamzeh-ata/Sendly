using System.Net;
using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;

namespace Sendly.Application.Services;

public static class PublicSearchHtmlParser
{
    private static readonly Regex AnchorRegex = new(
        "<a[^>]+href=\"(?<href>[^\"]+)\"[^>]*>(?<text>.*?)</a>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex SnippetRegex = new(
        "class=\"[^\"]*(?:result__snippet|b_caption|snippet)[^\"]*\"[^>]*>(?<text>.*?)</",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

    public static bool LooksLikeChallenge(string html)
    {
        return html.Contains("anomaly-modal", StringComparison.OrdinalIgnoreCase)
               || html.Contains("bots use", StringComparison.OrdinalIgnoreCase)
               || html.Contains("challenge to confirm", StringComparison.OrdinalIgnoreCase)
               || html.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase)
               || html.Contains("are you a robot", StringComparison.OrdinalIgnoreCase)
               || html.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase)
               || html.Contains("verify you are human", StringComparison.OrdinalIgnoreCase);
    }

    public static List<WebSearchHit> ParseDuckDuckGo(string html, int maxResults, string provider)
    {
        var hits = new List<WebSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AnchorRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            var url = UnwrapDuckDuckGoUrl(href);
            if (url is null || !seen.Add(url))
            {
                continue;
            }

            var title = CleanText(match.Groups["text"].Value);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            hits.Add(new WebSearchHit { Url = url, Title = title, Provider = provider });
            if (hits.Count >= maxResults)
            {
                break;
            }
        }

        ApplySnippets(html, hits);
        return hits;
    }

    private static readonly Regex BingHeadingRegex = new(
        "<h2[^>]*>\\s*<a[^>]+href=\"(?<href>[^\"]+)\"[^>]*>(?<text>.*?)</a>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static List<WebSearchHit> ParseBing(string html, int maxResults, string provider)
    {
        var hits = new List<WebSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in BingHeadingRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            var url = UnwrapBingUrl(href);
            if (url is null || !seen.Add(url))
            {
                continue;
            }

            var title = CleanText(match.Groups["text"].Value);
            if (string.IsNullOrWhiteSpace(title) || title.Length < 3)
            {
                continue;
            }

            hits.Add(new WebSearchHit { Url = url, Title = title, Provider = provider });
            if (hits.Count >= maxResults)
            {
                return FinishBing(html, hits);
            }
        }

        if (hits.Count > 0)
        {
            return FinishBing(html, hits);
        }

        foreach (Match match in AnchorRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (!IsExternalHttpUrl(href, "bing.com", out var url) || !seen.Add(url))
            {
                continue;
            }

            var title = CleanText(match.Groups["text"].Value);
            if (string.IsNullOrWhiteSpace(title) || title.Length < 3)
            {
                continue;
            }

            hits.Add(new WebSearchHit { Url = url, Title = title, Provider = provider });
            if (hits.Count >= maxResults)
            {
                break;
            }
        }

        ApplySnippets(html, hits);
        return hits;
    }

    private static readonly Regex BraveResultRegex = new(
        "<a[^>]+href=\"(?<href>https?://[^\"]+)\"[^>]*>\\s*<div class=\"site-name-wrapper[\\s\\S]*?<div class=\"title[^\"]*\" title=\"(?<title>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BraveSnippetRegex = new(
        "generic-snippet[^\"]*\"[^>]*>(?<text>[\\s\\S]*?)</div>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<WebSearchHit> ParseBrave(string html, int maxResults, string provider)
    {
        var hits = new List<WebSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snippets = new List<string>();
        foreach (Match match in BraveSnippetRegex.Matches(html))
        {
            var text = CleanText(match.Groups["text"].Value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                snippets.Add(text);
            }
        }

        var index = 0;
        foreach (Match match in BraveResultRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (!IsExternalHttpUrl(href, "search.brave.com", out var url) || !seen.Add(url))
            {
                continue;
            }

            if (url.Contains("brave.com", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = CleanText(WebUtility.HtmlDecode(match.Groups["title"].Value));
            if (string.IsNullOrWhiteSpace(title) || title.Length < 3)
            {
                continue;
            }

            hits.Add(new WebSearchHit
            {
                Url = url,
                Title = title,
                Snippet = index < snippets.Count ? snippets[index] : null,
                Provider = provider
            });
            index++;
            if (hits.Count >= maxResults)
            {
                break;
            }
        }

        return hits;
    }

    public static List<WebSearchHit> ParseMojeek(string html, int maxResults, string provider)
    {
        var hits = new List<WebSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AnchorRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (!IsExternalHttpUrl(href, "mojeek.com", out var url) || !seen.Add(url))
            {
                continue;
            }

            var title = CleanText(match.Groups["text"].Value);
            if (string.IsNullOrWhiteSpace(title) || title.Length < 3)
            {
                continue;
            }

            hits.Add(new WebSearchHit { Url = url, Title = title, Provider = provider });
            if (hits.Count >= maxResults)
            {
                break;
            }
        }

        return hits;
    }

    private static List<WebSearchHit> FinishBing(string html, List<WebSearchHit> hits)
    {
        ApplySnippets(html, hits);
        return hits;
    }

    private static string? UnwrapBingUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var uIndex = href.IndexOf("u=", StringComparison.OrdinalIgnoreCase);
        if (href.Contains("/ck/a", StringComparison.OrdinalIgnoreCase) && uIndex >= 0)
        {
            var encoded = href[(uIndex + 2)..];
            var amp = encoded.IndexOf('&');
            if (amp >= 0)
            {
                encoded = encoded[..amp];
            }

            var decoded = DecodeBingTarget(encoded);
            return decoded is not null && IsExternalHttpUrl(decoded, "bing.com", out var unwrapped) ? unwrapped : null;
        }

        return IsExternalHttpUrl(href, "bing.com", out var direct) ? direct : null;
    }

    private static string? DecodeBingTarget(string encoded)
    {
        var payload = encoded.StartsWith("a1", StringComparison.OrdinalIgnoreCase) ? encoded[2..] : encoded;
        payload = payload.Replace('-', '+').Replace('_', '/');
        var padding = payload.Length % 4;
        if (padding > 0)
        {
            payload = payload.PadRight(payload.Length + (4 - padding), '=');
        }

        try
        {
            var bytes = Convert.FromBase64String(payload);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void ApplySnippets(string html, List<WebSearchHit> hits)
    {
        var snippets = new List<string>();
        foreach (Match match in SnippetRegex.Matches(html))
        {
            var text = CleanText(match.Groups["text"].Value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                snippets.Add(text);
            }
        }

        for (var i = 0; i < hits.Count && i < snippets.Count; i++)
        {
            hits[i].Snippet = snippets[i];
        }
    }

    private static string? UnwrapDuckDuckGoUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var uddgIndex = href.IndexOf("uddg=", StringComparison.OrdinalIgnoreCase);
        if (uddgIndex >= 0)
        {
            var encoded = href[(uddgIndex + 5)..];
            var amp = encoded.IndexOf('&');
            if (amp >= 0)
            {
                encoded = encoded[..amp];
            }

            var decoded = WebUtility.UrlDecode(encoded);
            return IsExternalHttpUrl(decoded, "duckduckgo.com", out var unwrapped) ? unwrapped : null;
        }

        return IsExternalHttpUrl(href, "duckduckgo.com", out var direct) ? direct : null;
    }

    private static bool IsExternalHttpUrl(string? raw, string engineHostPart, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.Host.Contains(engineHostPart, StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("microsoft.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("google.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        url = uri.ToString();
        return true;
    }

    private static string CleanText(string raw)
    {
        var withoutTags = TagRegex.Replace(raw, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
