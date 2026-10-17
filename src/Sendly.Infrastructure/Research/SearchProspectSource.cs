using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class SearchProspectSource : IProspectSource
{
    private readonly IEnumerable<IWebSearchProvider> _searchProviders;
    private readonly WebsitePageCrawler _crawler;
    private readonly IAiResearchService _ai;
    private readonly ResearchOptions _options;
    private readonly BraveSearchOptions _braveOptions;
    private readonly SearXngOptions _searXngOptions;
    private readonly ILogger<SearchProspectSource> _logger;

    public SearchProspectSource(
        IEnumerable<IWebSearchProvider> searchProviders,
        WebsitePageCrawler crawler,
        IAiResearchService ai,
        IOptions<ResearchOptions> options,
        IOptions<BraveSearchOptions> braveOptions,
        IOptions<SearXngOptions> searXngOptions,
        ILogger<SearchProspectSource> logger)
    {
        _searchProviders = searchProviders;
        _crawler = crawler;
        _ai = ai;
        _options = options.Value;
        _braveOptions = braveOptions.Value;
        _searXngOptions = searXngOptions.Value;
        _logger = logger;
    }

    public string Name => "WebSearch";

    public bool IsEnabled =>
        _options.EnableSearchProvider
        && _searchProviders.Any(x => x.IsEnabled);

    public async Task<IReadOnlyList<ProspectCandidate>> DiscoverAsync(
        ProspectSourceContext context,
        CancellationToken cancellationToken)
    {
        var state = context.DiscoveryState;
        if (state.CompletedSourceNames.Contains(Name) || state.LimitsExhausted)
        {
            return [];
        }

        var maxResults = Math.Clamp(_options.MaxSearchResults, 1, 50);
        var maxPages = Math.Clamp(Math.Min(_options.MaxPagesPerCrawl, 2), 1, 10);
        var maxRequests = ResolveMaxSearchRequests();
        var absoluteMaxQueries = 200;
        var offsetPages = Math.Clamp(_options.PagesPerSearchOffset, 1, 5);

        EnsureQueries(context, state);
        if (!EnsureAvailableQuery(context, state, absoluteMaxQueries, maxRequests))
        {
            return [];
        }

        var batchTarget = ResolveBatchTarget(context);
        var result = new List<ProspectCandidate>();

        await ReportProgressSafeAsync(context, state, cancellationToken);

        while (result.Count < batchTarget
               && state.SearchQueriesExecuted < absoluteMaxQueries
               && state.SearchRequestsSuccessful + state.SearchRequestsFailed < maxRequests
               && !IsDurationExceeded(state)
               && !state.LimitsExhausted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!EnsureAvailableQuery(context, state, absoluteMaxQueries, maxRequests))
            {
                break;
            }

            var query = state.GeneratedQueries[state.NextQueryIndex];
            state.NextQueryIndex++;
            state.SearchQueriesExecuted++;

            var queryHits = await SearchAllProvidersAsync(context, state, query, maxResults, offsetPages, maxRequests, cancellationToken);
            await ReportProgressSafeAsync(context, state, cancellationToken);
            if (queryHits.Count == 0)
            {
                continue;
            }

            var ranked = RankHits(DeduplicateHits(queryHits), context);
            var hitsProcessed = 0;
            foreach (var hit in ranked)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Count >= batchTarget)
                {
                    break;
                }

                if (state.CandidatesDiscovered >= Math.Clamp(_options.MaxCandidatesPerRun, 1, 2000))
                {
                    MarkLimitsIfNeeded(state, "Maximum candidates limit reached");
                    break;
                }

                var extracted = await ExtractFromHitAsync(context, state, hit, maxPages, cancellationToken);
                foreach (var candidate in extracted)
                {
                    var key = BuildCandidateKey(candidate);
                    if (!state.SeenCandidateKeys.Add(key))
                    {
                        continue;
                    }

                    result.Add(candidate);
                    state.CandidatesDiscovered++;
                }

                hitsProcessed++;
                if (hitsProcessed % 2 == 0 || result.Count >= batchTarget)
                {
                    state.CandidatesAfterDeduplication = ProspectCandidateDeduplicator.DeduplicateAndMerge(result).Count;
                    await ReportProgressSafeAsync(context, state, cancellationToken);
                }
            }
        }

        FinalizeSourceLimits(state, absoluteMaxQueries, maxRequests);

        var merged = ProspectCandidateDeduplicator.DeduplicateAndMerge(result);
        state.CandidatesAfterDeduplication += merged.Count;
        _logger.LogInformation(
            "WebSearch batch produced {Count} candidates (queries={Queries}, results={Results})",
            merged.Count,
            state.SearchQueriesExecuted,
            state.SearchResultsReceived);
        return merged;
    }

    private async Task<List<WebSearchHit>> SearchAllProvidersAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        string query,
        int maxResults,
        int offsetPages,
        int maxRequests,
        CancellationToken cancellationToken)
    {
        var hits = new List<WebSearchHit>();
        foreach (var provider in _searchProviders)
        {
            if (!provider.IsEnabled)
            {
                continue;
            }

            state.ActiveProviderName ??= provider.Name;
            var providerHits = 0;
            for (var page = 0; page < offsetPages; page++)
            {
                if (state.SearchRequestsSuccessful + state.SearchRequestsFailed >= maxRequests)
                {
                    MarkLimitsIfNeeded(state, "Maximum search requests reached");
                    return hits;
                }

                try
                {
                    var pageHits = await provider.SearchAsync(
                        new WebSearchRequest
                        {
                            Query = query,
                            MaxResults = maxResults,
                            Offset = page * maxResults,
                            Keywords = context.Keywords,
                            Countries = context.Countries,
                            Country = context.Countries.FirstOrDefault()
                        },
                        cancellationToken);
                    state.SearchRequestsSuccessful++;
                    state.SearchResultsReceived += pageHits.Count;
                    providerHits += pageHits.Count;
                    hits.AddRange(pageHits);
                    _logger.LogInformation(
                        "Search provider {Provider} query '{Query}' offset {Offset} returned {Count}",
                        provider.Name,
                        query,
                        page * maxResults,
                        pageHits.Count);

                    if (pageHits.Count == 0)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    state.SearchRequestsFailed++;
                    RecordProviderFailure(state, ex);
                    if (ex is TaskCanceledException or TimeoutException)
                    {
                        state.TimeoutCount++;
                    }

                    _logger.LogWarning(ex, "Search provider failed {Provider}", provider.Name);
                    break;
                }
            }

            if (providerHits > 0)
            {
                return hits;
            }
        }

        return hits;
    }

    private async Task<List<ProspectCandidate>> ExtractFromHitAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        WebSearchHit hit,
        int maxPages,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri))
        {
            return [];
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return [];
        }

        var hostKey = ResearchDirectoryCatalog.BuildSeenKey(uri);
        if (!state.SeenHosts.Add(hostKey))
        {
            return [];
        }

        var seedCandidate = BuildSeedCandidateFromHit(context, hit, uri);
        var result = new List<ProspectCandidate> { seedCandidate };
        if (ResearchDirectoryCatalog.IsDirectoryUrl(uri))
        {
            return result;
        }

        try
        {
            var crawled = await _crawler.ExtractCandidatesAsync(
                _ai,
                context,
                uri,
                seedCandidate.CompanyName,
                context.Countries.FirstOrDefault(),
                context.Industries.FirstOrDefault(),
                $"Web search ({hit.Title ?? uri.Host})",
                maxPages,
                cancellationToken);

            if (crawled.Count > 0)
            {
                result.AddRange(crawled);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search crawl failed for {Url}", uri);
        }

        return ProspectCandidateDeduplicator.DeduplicateAndMerge(result);
    }

    private static ProspectCandidate BuildSeedCandidateFromHit(
        ProspectSourceContext context,
        WebSearchHit hit,
        Uri uri)
    {
        var isDirectory = ResearchDirectoryCatalog.IsDirectoryUrl(uri);
        var companyName = isDirectory
            ? ResearchDirectoryCatalog.InferDirectoryCompanyName(hit.Title, uri) ?? InferCompanyName(hit.Title, uri)
            : InferCompanyName(hit.Title, uri);
        var city = InferCity(hit, context);
        return new ProspectCandidate
        {
            CompanyName = companyName,
            Website = isDirectory ? null : uri.GetLeftPart(UriPartial.Authority),
            Country = context.Countries.FirstOrDefault(),
            City = city,
            Industry = context.Industries.FirstOrDefault(),
            CompanyDescription = hit.Snippet,
            SourceUrl = uri.ToString(),
            SourceType = isDirectory ? ProspectSourceType.Directory : ProspectSourceType.Search,
            SourceName = isDirectory
                ? $"Directory ({uri.Host})"
                : hit.Provider ?? "Web search",
            ResearchNotes = isDirectory
                ? "Discovered from a known directory listing. Contact details and school website may still be researched."
                : "Discovered from search result. Contact details may still be researched."
        };
    }

    private static string InferCompanyName(string? title, Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            var cleaned = title.Split('|', '-', '–', ':')[0].Trim();
            if (!string.IsNullOrWhiteSpace(cleaned) && cleaned.Length >= 2)
            {
                return cleaned;
            }
        }

        var host = uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? uri.Host : parts[0];
    }

    private static string? InferCity(WebSearchHit hit, ProspectSourceContext context)
    {
        var text = $"{hit.Title} {hit.Snippet}";
        foreach (var location in ResearchSearchQueryGenerator.ResolveLocations(context))
        {
            if (!string.IsNullOrWhiteSpace(location)
                && text.Contains(location, StringComparison.OrdinalIgnoreCase))
            {
                return location;
            }
        }

        return null;
    }

    private void EnsureQueries(ProspectSourceContext context, ResearchDiscoveryState state)
    {
        if (state.GeneratedQueries.Count > 0)
        {
            return;
        }

        var maxQueries = Math.Clamp(_options.MaxSearchQueries, 1, 200);
        state.GeneratedQueries = ResearchSearchQueryGenerator.Generate(context, maxQueries).ToList();
        _logger.LogInformation("Generated {Count} research search queries", state.GeneratedQueries.Count);
    }

    private bool EnsureAvailableQuery(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        int absoluteMaxQueries,
        int maxRequests)
    {
        if (state.LimitsExhausted || state.CompletedSourceNames.Contains(Name))
        {
            return false;
        }

        if (IsDurationExceeded(state))
        {
            CompleteWithLimit(state, "Maximum research duration reached");
            return false;
        }

        if (state.SearchRequestsSuccessful + state.SearchRequestsFailed >= maxRequests)
        {
            CompleteWithLimit(state, "Maximum search requests reached");
            return false;
        }

        if (state.SearchQueriesExecuted >= absoluteMaxQueries)
        {
            CompleteWithLimit(state, "Maximum search queries reached");
            return false;
        }

        if (state.NextQueryIndex < state.GeneratedQueries.Count)
        {
            return true;
        }

        if (TryExpandQueries(context, state, absoluteMaxQueries))
        {
            return state.NextQueryIndex < state.GeneratedQueries.Count;
        }

        CompleteWithLimit(state, "All search queries exhausted");
        return false;
    }

    private bool TryExpandQueries(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        int absoluteMaxQueries)
    {
        var room = absoluteMaxQueries - state.GeneratedQueries.Count;
        if (room <= 0)
        {
            return false;
        }

        var waveSize = Math.Clamp(_options.MaxSearchQueries, 1, 200);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var round = state.QueryExpansionRound;
            var extra = ResearchSearchQueryGenerator.GenerateAdditional(
                context,
                state.GeneratedQueries,
                Math.Min(room, waveSize),
                round);
            state.QueryExpansionRound = round + 1;
            if (extra.Count == 0)
            {
                continue;
            }

            state.GeneratedQueries.AddRange(extra);
            state.CompletedSourceNames.Remove(Name);
            _logger.LogInformation(
                "Expanded research search queries by {Count} (total={Total}, round={Round})",
                extra.Count,
                state.GeneratedQueries.Count,
                round);
            return true;
        }

        return false;
    }

    private void FinalizeSourceLimits(
        ResearchDiscoveryState state,
        int absoluteMaxQueries,
        int maxRequests)
    {
        if (state.LimitsExhausted || state.CompletedSourceNames.Contains(Name))
        {
            return;
        }

        if (IsDurationExceeded(state))
        {
            CompleteWithLimit(state, "Maximum research duration reached");
            return;
        }

        if (state.SearchRequestsSuccessful + state.SearchRequestsFailed >= maxRequests)
        {
            CompleteWithLimit(state, "Maximum search requests reached");
            return;
        }

        if (state.SearchQueriesExecuted >= absoluteMaxQueries)
        {
            CompleteWithLimit(state, "Maximum search queries reached");
        }
    }

    private static void CompleteWithLimit(ResearchDiscoveryState state, string reason)
    {
        state.CompletedSourceNames.Add("WebSearch");
        MarkLimitsIfNeeded(state, reason);
    }

    private int ResolveMaxSearchRequests()
    {
        var researchCap = Math.Clamp(_options.MaxSearchRequests, 1, 500);
        var providerBudget = 0;
        if (_braveOptions.Enabled)
        {
            providerBudget += Math.Clamp(_braveOptions.MaxRequestsPerResearchRun, 1, 500);
        }

        if (_searXngOptions.Enabled)
        {
            providerBudget += Math.Clamp(_searXngOptions.MaxRequestsPerResearchRun, 1, 500);
        }

        if (providerBudget <= 0)
        {
            return researchCap;
        }

        return Math.Min(researchCap, providerBudget);
    }

    private static int ResolveBatchTarget(ProspectSourceContext context)
    {
        if (context.DiscoveryBatchSize > 0)
        {
            return Math.Clamp(context.DiscoveryBatchSize, 5, 25);
        }

        return 10;
    }

    private async Task ReportProgressSafeAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        if (context.ReportProgressAsync is null)
        {
            return;
        }

        try
        {
            await context.ReportProgressAsync(state, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Research discovery progress notification failed");
        }
    }

    private bool IsDurationExceeded(ResearchDiscoveryState state)
    {
        var minutes = Math.Clamp(_options.MaxResearchDurationMinutes, 1, 240);
        return DateTimeOffset.UtcNow - state.StartedAtUtc > TimeSpan.FromMinutes(minutes);
    }

    private static void MarkLimitsIfNeeded(ResearchDiscoveryState state, string reason)
    {
        state.LimitsExhausted = true;
        state.LimitsExhaustedReason ??= reason;
    }

    private static void RecordProviderFailure(ResearchDiscoveryState state, Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        if (message.Contains("401", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
        {
            state.Http401Count++;
        }
        else if (message.Contains("429", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
        {
            state.Http429Count++;
        }
        else if (message.Contains("500", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("502", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("503", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("504", StringComparison.OrdinalIgnoreCase))
        {
            state.Http5xxCount++;
        }
        else if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                 || ex is TaskCanceledException or TimeoutException)
        {
            state.TimeoutCount++;
        }
    }

    private static string BuildCandidateKey(ProspectCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.SourceUrl)
            && Uri.TryCreate(candidate.SourceUrl, UriKind.Absolute, out var sourceUri)
            && ResearchDirectoryCatalog.IsDirectoryUrl(sourceUri))
        {
            return ResearchDirectoryCatalog.BuildCandidateKey(sourceUri, candidate.CompanyName);
        }

        var website = ProspectNormalizer.NormalizeWebsite(candidate.Website)
                      ?? ProspectNormalizer.NormalizeWebsite(candidate.SourceUrl)
                      ?? string.Empty;
        var company = ProspectNormalizer.NormalizeCompanyName(candidate.CompanyName);
        var city = ProspectNormalizer.NormalizeCity(candidate.City) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(website)
            && !ResearchDirectoryCatalog.IsDirectoryHost(website))
        {
            return $"site:{website}";
        }

        return $"name:{company}|city:{city}";
    }

    private List<WebSearchHit> RankHits(List<WebSearchHit> hits, ProspectSourceContext context)
    {
        var excluded = new List<string>();
        excluded.AddRange(context.ExcludedKeywords);
        excluded.AddRange(context.ExcludedIndustries);

        var ranked = WebSearchHitRanker.RankAndFilter(
            hits,
            x => x.Url,
            x => x.Title,
            x => x.Snippet,
            context.Keywords,
            context.Industries,
            context.Countries,
            excluded,
            _options.HostDenylist,
            (hit, score) => hit.Score = score);

        foreach (var hit in ranked)
        {
            if (Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
                && ResearchDirectoryCatalog.IsDirectoryUrl(uri))
            {
                hit.Score += 20;
            }
        }

        ranked = ranked
            .OrderByDescending(x => x.Score)
            .ToList();

        var minScore = Math.Max(0, _options.MinSearchRelevanceScore);
        if (minScore <= 0)
        {
            return ranked;
        }

        return ranked.Where(x => x.Score >= minScore).ToList();
    }

    private static List<WebSearchHit> DeduplicateHits(List<WebSearchHit> hits)
    {
        var result = new List<WebSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in hits)
        {
            if (string.IsNullOrWhiteSpace(hit.Url))
            {
                continue;
            }

            var key = ProspectNormalizer.NormalizeUrl(hit.Url) ?? hit.Url.Trim().ToLowerInvariant();
            if (seen.Add(key))
            {
                result.Add(hit);
            }
        }

        return result;
    }
}
