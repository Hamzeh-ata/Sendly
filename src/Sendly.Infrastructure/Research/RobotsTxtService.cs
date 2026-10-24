using System.Collections.Concurrent;
using System.Net;
using Sendly.Application.Abstractions;
using Sendly.Domain.Services;

namespace Sendly.Infrastructure.Research;

public class RobotsTxtService : IRobotsTxtService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, RobotsRules> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RobotsTxtService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> IsAllowedAsync(Uri url, string userAgent, CancellationToken cancellationToken)
    {
        var rules = await LoadRulesAsync(url, userAgent, cancellationToken);
        return rules.IsAllowed(url.AbsolutePath);
    }

    public async Task<IReadOnlyList<Uri>> GetSitemapUrlsAsync(
        Uri siteUrl,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var rules = await LoadRulesAsync(siteUrl, userAgent, cancellationToken);
        return rules.SitemapUrls;
    }

    private async Task<RobotsRules> LoadRulesAsync(
        Uri url,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var origin = $"{url.Scheme}://{url.Authority}".ToLowerInvariant();
        if (_cache.TryGetValue(origin, out var cached))
        {
            return cached;
        }

        var robotsUri = new Uri(new Uri(origin + "/"), "robots.txt");
        if (await SsrfGuard.IsBlockedUriAsync(robotsUri, cancellationToken))
        {
            var blocked = RobotsRules.AllowAll();
            _cache[origin] = blocked;
            return blocked;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("ResearchCrawler");
            using var request = new HttpRequestMessage(HttpMethod.Get, robotsUri);
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var allowAll = RobotsRules.AllowAll();
                _cache[origin] = allowAll;
                return allowAll;
            }

            if (!response.IsSuccessStatusCode)
            {
                var allowAll = RobotsRules.AllowAll();
                _cache[origin] = allowAll;
                return allowAll;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = RobotsRules.Parse(body, userAgent, origin);
            _cache[origin] = parsed;
            return parsed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            var allowAll = RobotsRules.AllowAll();
            _cache[origin] = allowAll;
            return allowAll;
        }
    }

    private sealed class RobotsRules
    {
        private readonly List<string> _allows = [];
        private readonly List<string> _disallows = [];

        public List<Uri> SitemapUrls { get; } = [];

        public static RobotsRules AllowAll() => new();

        public static RobotsRules Parse(string content, string userAgent, string origin)
        {
            var rules = new RobotsRules();
            var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var inRelevantGroup = false;
            var sawUserAgent = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();

                if (key.Equals("User-agent", StringComparison.OrdinalIgnoreCase))
                {
                    sawUserAgent = true;
                    inRelevantGroup = value == "*"
                        || userAgent.Contains(value, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (key.Equals("Sitemap", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(value, UriKind.Absolute, out var sitemapUri)
                        && (sitemapUri.Scheme == Uri.UriSchemeHttp || sitemapUri.Scheme == Uri.UriSchemeHttps))
                    {
                        rules.SitemapUrls.Add(sitemapUri);
                    }
                    else if (Uri.TryCreate(new Uri(origin + "/"), value, out var relativeSitemap))
                    {
                        rules.SitemapUrls.Add(relativeSitemap);
                    }

                    continue;
                }

                if (!sawUserAgent || !inRelevantGroup)
                {
                    continue;
                }

                if (key.Equals("Allow", StringComparison.OrdinalIgnoreCase))
                {
                    rules._allows.Add(value);
                }
                else if (key.Equals("Disallow", StringComparison.OrdinalIgnoreCase))
                {
                    rules._disallows.Add(value);
                }
            }

            return rules;
        }

        public bool IsAllowed(string path)
        {
            if (_disallows.Count == 0)
            {
                return true;
            }

            var matchedDisallow = LongestMatch(_disallows, path);
            if (matchedDisallow is null)
            {
                return true;
            }

            var matchedAllow = LongestMatch(_allows, path);
            if (matchedAllow is null)
            {
                return matchedDisallow.Length == 0;
            }

            return matchedAllow.Length >= matchedDisallow.Length;
        }

        private static string? LongestMatch(List<string> patterns, string path)
        {
            string? best = null;
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrEmpty(pattern))
                {
                    continue;
                }

                if (path.StartsWith(pattern, StringComparison.Ordinal))
                {
                    if (best is null || pattern.Length > best.Length)
                    {
                        best = pattern;
                    }
                }
            }

            return best;
        }
    }
}
