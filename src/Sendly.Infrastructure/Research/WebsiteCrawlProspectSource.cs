using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class WebsiteCrawlProspectSource : IProspectSource
{
    private readonly WebsitePageCrawler _crawler;
    private readonly IAiResearchService _ai;
    private readonly ResearchOptions _options;
    private readonly ILogger<WebsiteCrawlProspectSource> _logger;

    public WebsiteCrawlProspectSource(
        WebsitePageCrawler crawler,
        IAiResearchService ai,
        IOptions<ResearchOptions> options,
        ILogger<WebsiteCrawlProspectSource> logger)
    {
        _crawler = crawler;
        _ai = ai;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "WebsiteCrawler";

    public bool IsEnabled => _options.EnableWebsiteCrawler && _options.CrawlTargets.Count > 0;

    public async Task<IReadOnlyList<ProspectCandidate>> DiscoverAsync(
        ProspectSourceContext context,
        CancellationToken cancellationToken)
    {
        if (context.DiscoveryState.CompletedSourceNames.Contains(Name))
        {
            return [];
        }

        var result = new List<ProspectCandidate>();
        var maxPages = Math.Clamp(_options.MaxPagesPerCrawl, 1, 20);

        foreach (var target in _options.CrawlTargets)
        {
            if (string.IsNullOrWhiteSpace(target.Url) || !Uri.TryCreate(target.Url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            try
            {
                var candidates = await _crawler.ExtractCandidatesAsync(
                    _ai,
                    context,
                    uri,
                    target.CompanyName,
                    target.Country,
                    target.Industry,
                    string.IsNullOrWhiteSpace(target.SourceName) ? "Company website" : target.SourceName,
                    maxPages,
                    cancellationToken);

                foreach (var candidate in candidates)
                {
                    result.Add(candidate);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Website crawl provider failed for {Url}", uri);
            }
        }

        context.DiscoveryState.CompletedSourceNames.Add(Name);
        context.DiscoveryState.CandidatesDiscovered += result.Count;
        return result;
    }
}
