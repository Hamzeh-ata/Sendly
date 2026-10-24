using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class CommonCrawlIndexDiscoverySource : IUrlDiscoverySource
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResearchOptions _options;
    private readonly ILogger<CommonCrawlIndexDiscoverySource> _logger;

    public CommonCrawlIndexDiscoverySource(
        IHttpClientFactory httpClientFactory,
        IOptions<ResearchOptions> options,
        ILogger<CommonCrawlIndexDiscoverySource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "CommonCrawlIndex";

    public bool IsEnabled(UrlDiscoveryContext context)
        => _options.EnableCommonCrawlDiscovery
           && context.EnableWebArchiveDiscovery
           && context.DiscoveryStage >= 2;

    public async Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var queries = BuildQueries(context);
        if (queries.Count == 0)
        {
            return [];
        }

        var client = _httpClientFactory.CreateClient("ResearchCrawler");
        var result = new List<DiscoveredUrlCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limit = Math.Clamp(
            context.MaxArchiveRecords > 0 ? context.MaxArchiveRecords : context.MaxUrls / 4,
            0,
            100);
        if (limit == 0)
        {
            return [];
        }

        foreach (var query in queries.Take(2))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url =
                    $"https://index.commoncrawl.org/CC-MAIN-2024-10-index?url={Uri.EscapeDataString(query)}&output=json&fl=url&limit={Math.Min(limit, 20)}";
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
                using var response = await client.GetAsync(url, timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Common Crawl index returned {Status} for query {Query}",
                        (int)response.StatusCode,
                        query);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!TryReadUrl(line, out var foundUrl))
                    {
                        continue;
                    }

                    var canonical = UrlCanonicalizer.Canonicalize(foundUrl);
                    if (string.IsNullOrWhiteSpace(canonical) || !seen.Add(canonical))
                    {
                        continue;
                    }

                    if (await SsrfGuard.IsBlockedUriAsync(new Uri(canonical), cancellationToken))
                    {
                        continue;
                    }

                    if (DomainHealthPolicy.ShouldRejectArchiveCandidate(
                            canonical,
                            context.Industries,
                            null))
                    {
                        continue;
                    }

                    var host = UrlCanonicalizer.ExtractDomain(canonical);
                    if (!string.IsNullOrWhiteSpace(host)
                        && result.Count(x => string.Equals(
                            UrlCanonicalizer.ExtractDomain(x.Url),
                            host,
                            StringComparison.OrdinalIgnoreCase)) >= 2)
                    {
                        continue;
                    }

                    result.Add(new DiscoveredUrlCandidate
                    {
                        Url = canonical,
                        SourceType = DiscoverySourceType.WebArchive,
                        SourceName = "Common Crawl public index",
                        Depth = 0
                    });
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Common Crawl discovery timed out for query {Query}", query);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Common Crawl discovery failed for query {Query}", query);
            }
        }

        return result;
    }

    private static List<string> BuildQueries(UrlDiscoveryContext context)
    {
        var queries = new List<string>();
        var tlds = ResolveCountryTlds(context.Countries);
        var industries = context.Industries
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant().Replace(' ', '-'))
            .Where(x => x.Length >= 3)
            .Take(3)
            .ToList();

        foreach (var tld in tlds)
        {
            if (industries.Count == 0)
            {
                queries.Add($"*.{tld}/*");
                continue;
            }

            foreach (var industry in industries)
            {
                queries.Add($"*.{tld}/*{industry}*");
            }
        }

        return queries;
    }

    private static List<string> ResolveCountryTlds(List<string> countries)
    {
        var tlds = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var country in countries)
        {
            var key = country.Trim().ToLowerInvariant();
            var tld = key switch
            {
                "saudi arabia" or "sa" or "ksa" => "sa",
                "united arab emirates" or "uae" or "ae" or "dubai" => "ae",
                "egypt" or "eg" => "eg",
                "jordan" or "jo" => "jo",
                "united kingdom" or "uk" or "gb" => "uk",
                "united states" or "usa" or "us" => "com",
                "new zealand" or "nz" or "aotearoa" => "nz",
                "australia" or "au" => "au",
                "canada" or "ca" => "ca",
                _ => null
            };

            if (tld is not null && seen.Add(tld))
            {
                tlds.Add(tld);
            }
        }

        return tlds;
    }

    private static bool TryReadUrl(string jsonLine, out string url)
    {
        url = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            if (doc.RootElement.TryGetProperty("url", out var prop))
            {
                url = prop.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(url);
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
