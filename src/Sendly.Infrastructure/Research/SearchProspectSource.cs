using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
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
    private readonly ISendlyDbContext? _db;
    private readonly IResearchEventRecorder? _events;
    private readonly ILocalAiQueryPlanner? _queryPlanner;
    private readonly ILocalAiSearchClassifier? _searchClassifier;
    private readonly ILocalAiResearchIntelligence? _researchIntelligence;
    private readonly AdaptiveResearchPlanner? _adaptivePlanner;
    private ResearchBudgetManager? _budget;

    public SearchProspectSource(
        IEnumerable<IWebSearchProvider> searchProviders,
        WebsitePageCrawler crawler,
        IAiResearchService ai,
        IOptions<ResearchOptions> options,
        IOptions<BraveSearchOptions> braveOptions,
        IOptions<SearXngOptions> searXngOptions,
        ILogger<SearchProspectSource> logger,
        ISendlyDbContext? db = null,
        IResearchEventRecorder? events = null,
        ILocalAiQueryPlanner? queryPlanner = null,
        ILocalAiSearchClassifier? searchClassifier = null,
        ILocalAiResearchIntelligence? researchIntelligence = null,
        AdaptiveResearchPlanner? adaptivePlanner = null)
    {
        _searchClassifier = searchClassifier;
        _queryPlanner = queryPlanner;
        _researchIntelligence = researchIntelligence;
        _adaptivePlanner = adaptivePlanner;
        _searchProviders = searchProviders;
        _crawler = crawler;
        _ai = ai;
        _options = options.Value;
        _braveOptions = braveOptions.Value;
        _searXngOptions = searXngOptions.Value;
        _logger = logger;
        _db = db;
        _events = events;
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

        _budget = BuildBudget(state);
        var maxResults = Math.Clamp(_options.MaxSearchResults, 1, 50);
        var maxPages = Math.Clamp(Math.Min(_options.MaxPagesPerCrawl, 2), 1, 10);
        var maxRequests = ResolveMaxSearchRequests();
        var absoluteMaxQueries = 200;
        var offsetPages = Math.Clamp(_options.PagesPerSearchOffset, 1, 5);

        await EnsureQueriesAsync(context, state, cancellationToken);
        await AppendAdaptiveQueriesAsync(context, state, cancellationToken);
        if (!await EnsureSearchProviderHealthyAsync(context, state, cancellationToken))
        {
            return [];
        }

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

            var planned = TakeNextPlannedQuery(context, state);
            if (planned is null)
            {
                break;
            }

            var query = planned.Query;
            state.SearchQueriesExecuted++;
            Increment(state.QueriesExecutedByIndustry, planned.Industry);
            state.CurrentInvestigationActivity = "searching_for_sources";
            state.LastHeartbeatAtUtc = DateTimeOffset.UtcNow;
            await RecordSearchEventAsync(
                context,
                ResearchRunEventType.SearchQueryStarted,
                $"Search query started: {query}",
                planned,
                null,
                cancellationToken);

            var queryHits = await SearchAllProvidersAsync(
                context,
                state,
                planned,
                maxResults,
                offsetPages,
                maxRequests,
                cancellationToken);
            await ReportProgressSafeAsync(context, state, cancellationToken);
            RecordQueryYield(state, planned, queryHits, context);
            await PersistSearchHitsAsync(context, planned, queryHits, cancellationToken);
            if (queryHits.Count == 0)
            {
                if (state.SearchProviderHealth is SearchProviderHealthState.Unavailable
                    or SearchProviderHealthState.InvalidResponse)
                {
                    state.ConsecutiveProviderFailures++;
                    if (state.ConsecutiveProviderFailures >= 2)
                    {
                        StopAfterSearchFailures(state);
                        break;
                    }
                }
                else
                {
                    state.ConsecutiveProviderFailures = 0;
                }

                continue;
            }

            state.ConsecutiveProviderFailures = 0;

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
                    MarkRunLevelLimit(state, "Maximum candidates limit reached");
                    break;
                }

                var extracted = await ExtractFromHitAsync(context, state, hit, planned, maxPages, cancellationToken);
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
        PlannedSearchQuery planned,
        int maxResults,
        int offsetPages,
        int maxRequests,
        CancellationToken cancellationToken)
    {
        var hits = new List<WebSearchHit>();
        var query = planned.Query;
        var startedAt = DateTimeOffset.UtcNow;
        SearchProviderHealthState worstState = SearchProviderHealthState.NoResults;
        string? errorSummary = null;
        var anyAttempt = false;
        foreach (var provider in _searchProviders)
        {
            if (!provider.IsEnabled)
            {
                continue;
            }

            anyAttempt = true;
            state.ActiveProviderName ??= provider.Name;
            var providerHits = 0;
            for (var page = 0; page < offsetPages; page++)
            {
                if (state.SearchRequestsSuccessful + state.SearchRequestsFailed >= maxRequests)
                {
                    MarkLimitsIfNeeded(state, "Maximum search requests reached");
                    await RecordQueryCompletionAsync(
                        context,
                        planned,
                        hits,
                        worstState,
                        errorSummary,
                        startedAt,
                        cancellationToken);
                    return hits;
                }

                try
                {
                    var outcome = await ExecuteProviderSearchAsync(
                        provider,
                        new WebSearchRequest
                        {
                            Query = query,
                            MaxResults = maxResults,
                            Offset = page * maxResults,
                            Language = planned.Language,
                            Keywords = context.Keywords,
                            Countries = context.Countries,
                            Country = context.Countries.FirstOrDefault()
                        },
                        cancellationToken);
                    ApplyOutcomeToState(state, outcome);
                    worstState = CombineStates(worstState, outcome.State);
                    if (!string.IsNullOrWhiteSpace(outcome.ErrorSummary))
                    {
                        errorSummary = outcome.ErrorSummary;
                    }

                    await RecordSearchEventAsync(
                        context,
                        ResearchRunEventType.SearchProviderAttempted,
                        $"{provider.Name} returned {outcome.ProviderResultCount} results ({outcome.State})",
                        planned,
                        new
                        {
                            provider = provider.Name,
                            state = outcome.State.ToString(),
                            providerResultCount = outcome.ProviderResultCount,
                            acceptedCount = outcome.AcceptedCount,
                            error = outcome.ErrorSummary,
                            unresponsiveEngines = outcome.UnresponsiveEngines
                        },
                        cancellationToken,
                        provider.Name);

                    providerHits += outcome.Hits.Count;
                    hits.AddRange(outcome.Hits);
                    _logger.LogInformation(
                        "Search provider {Provider} query '{Query}' offset {Offset} returned {Count} state {State}",
                        provider.Name,
                        query,
                        page * maxResults,
                        outcome.Hits.Count,
                        outcome.State);

                    if (outcome.Hits.Count == 0
                        || outcome.State is SearchProviderHealthState.Unavailable
                            or SearchProviderHealthState.InvalidResponse)
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

                    worstState = SearchProviderHealthState.Unavailable;
                    errorSummary = ex.Message;
                    _logger.LogWarning(ex, "Search provider failed {Provider}", provider.Name);
                    await RecordSearchEventAsync(
                        context,
                        ResearchRunEventType.SearchQueryFailed,
                        $"{provider.Name} failed: {ex.Message}",
                        planned,
                        new { provider = provider.Name, error = ex.Message },
                        cancellationToken,
                        provider.Name);
                    break;
                }
            }

            if (providerHits > 0)
            {
                await RecordQueryCompletionAsync(
                    context,
                    planned,
                    hits,
                    SearchProviderHealthState.Healthy,
                    null,
                    startedAt,
                    cancellationToken);
                return hits;
            }
        }

        if (!anyAttempt)
        {
            worstState = SearchProviderHealthState.Unavailable;
            errorSummary = "No search providers are enabled.";
            state.SearchProviderHealth = worstState;
        }

        await RecordQueryCompletionAsync(
            context,
            planned,
            hits,
            worstState,
            errorSummary,
            startedAt,
            cancellationToken);
        return hits;
    }

    private static async Task<WebSearchOutcome> ExecuteProviderSearchAsync(
        IWebSearchProvider provider,
        WebSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (provider is IDiagnosticWebSearchProvider diagnostic)
        {
            return await diagnostic.SearchDetailedAsync(request, cancellationToken);
        }

        var hits = await provider.SearchAsync(request, cancellationToken);
        return WebSearchOutcome.FromHits(provider.Name, hits);
    }

    private static void ApplyOutcomeToState(ResearchDiscoveryState state, WebSearchOutcome outcome)
    {
        var combined = CombineStates(state.SearchProviderHealth, outcome.State);
        state.SearchProviderHealth = combined;
        if (combined == SearchProviderHealthState.Healthy)
        {
            state.SearchProviderHealthSummary = null;
        }
        else if (combined == outcome.State && !string.IsNullOrWhiteSpace(outcome.ErrorSummary))
        {
            state.SearchProviderHealthSummary = outcome.ErrorSummary;
        }

        if (outcome.State is SearchProviderHealthState.Unavailable or SearchProviderHealthState.InvalidResponse)
        {
            state.SearchRequestsFailed++;
            return;
        }

        state.SearchRequestsSuccessful++;
        state.SearchResultsReceived += outcome.Hits.Count;
    }

    private static void StopAfterSearchFailures(ResearchDiscoveryState state)
    {
        if (state.SearchResultsReceived > 0)
        {
            CompleteWithLimit(state, "No additional candidates could be discovered.");
            state.DiscoveryStopReason = DiscoveryStopReason.NoCandidatesDiscovered;
            state.CurrentInvestigationActivity = "search_exhausted";
            return;
        }

        CompleteWithLimit(state, "Search provider unavailable");
        state.DiscoveryStopReason = DiscoveryStopReason.SearchProviderUnavailable;
        state.CurrentInvestigationActivity = "search_provider_unhealthy";
    }

    private static SearchProviderHealthState CombineStates(
        SearchProviderHealthState current,
        SearchProviderHealthState incoming)
    {
        if (HealthSeverity(incoming) < HealthSeverity(current))
        {
            return incoming;
        }

        if (HealthSeverity(current) < HealthSeverity(incoming))
        {
            return current;
        }

        if (incoming == SearchProviderHealthState.Healthy
            || current == SearchProviderHealthState.Healthy)
        {
            return SearchProviderHealthState.Healthy;
        }

        if (incoming == SearchProviderHealthState.ResultsFiltered
            || current == SearchProviderHealthState.ResultsFiltered)
        {
            return SearchProviderHealthState.ResultsFiltered;
        }

        return SearchProviderHealthState.NoResults;
    }

    private static int HealthSeverity(SearchProviderHealthState state)
        => state switch
        {
            SearchProviderHealthState.Healthy => 0,
            SearchProviderHealthState.ResultsFiltered => 1,
            SearchProviderHealthState.NoResults => 2,
            SearchProviderHealthState.Degraded => 3,
            SearchProviderHealthState.InvalidResponse => 4,
            SearchProviderHealthState.Unavailable => 5,
            _ => 6
        };

    private ResearchBudgetManager BuildBudget(ResearchDiscoveryState state)
    {
        var limits = new ResearchBudgetLimits
        {
            SearchRequests = ResolveMaxSearchRequests(),
            VerificationFetches = Math.Clamp(_options.MaxVerificationFetchesPerRun, 0, 500),
            ContactFetches = Math.Clamp(_options.MaxContactFetchesPerRun, 0, 500),
            LinkExpansionFetches = Math.Clamp(_options.MaxLinkExpansionFetchesPerRun, 0, 500),
            OllamaCalls = Math.Clamp(_options.MaxOllamaCallsPerRun, 0, 500),
            MaxDurationMinutes = Math.Clamp(_options.MaxResearchDurationMinutes, 1, 240)
        };
        var usage = new ResearchBudgetUsage
        {
            SearchRequests = state.SearchRequestsSuccessful + state.SearchRequestsFailed,
            OllamaCalls = state.OllamaCallsUsed
        };
        return new ResearchBudgetManager(limits, state.StartedAtUtc, usage);
    }

    private async Task<List<ProspectCandidate>> ExtractFromHitAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        WebSearchHit hit,
        PlannedSearchQuery planned,
        int maxPages,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri))
        {
            state.RecordRejection("UnsupportedOrInvalidResult");
            return [];
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            state.RecordRejection("UnsupportedOrInvalidResult");
            return [];
        }

        var spec = ProjectEligibilityPolicy.RequireSpec(context);
        var eligibility = ProjectEligibilityPolicy.EvaluateSearchHit(BuildGateInput(context, hit, planned), spec);
        if (!eligibility.IsEligible && !eligibility.MayFetch)
        {
            state.RecordRejection(MapDispositionRejection(eligibility.Disposition));
            state.RecordUrlRejection(eligibility.ReasonCode);
            return [];
        }

        var gate = SearchResultGate.Evaluate(BuildGateInput(context, hit, planned));
        if (gate.Decision == SearchResultDecision.Ambiguous)
        {
            gate = await ResolveAmbiguousAsync(context, state, hit, planned, gate, cancellationToken);
        }

        if (!gate.MayFetch && gate.Decision != SearchResultDecision.Ambiguous)
        {
            state.RecordRejection(gate.Decision.ToString());
            state.RecordUrlRejection("gate." + gate.Decision);
            return [];
        }

        var hostKey = ResearchDirectoryCatalog.BuildSeenKey(uri);
        if (!state.SeenHosts.Add(hostKey))
        {
            state.RecordRejection("Duplicate");
            return [];
        }

        var isDirectory = gate.IsDirectory || SearchResultGate.IsDirectoryUrl(uri);
        var fetchPages = ResolveFetchPages(gate, maxPages);
        if (fetchPages <= 0)
        {
            state.RecordRejection("BudgetExhausted");
            return [];
        }

        try
        {
            var crawled = await _crawler.ExtractCandidatesAsync(
                _ai,
                context,
                uri,
                null,
                null,
                null,
                isDirectory ? $"Directory ({uri.Host})" : $"Web search ({hit.Provider ?? uri.Host})",
                fetchPages,
                cancellationToken);
            await EnrichCandidatesWithEvidenceAsync(context, state, crawled, hit, uri, cancellationToken);
            var verified = VerifyCrawledCandidates(context, state, crawled, hit, uri, isDirectory, gate);
            return ProspectCandidateDeduplicator.DeduplicateAndMerge(verified)
                .Where(HasMinimumProspectEvidence)
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.RecordRejection("VerificationFetchFailed");
            _logger.LogWarning(ex, "Search verification failed for {Url}", uri);
            return [];
        }
    }

    private int ResolveFetchPages(SearchResultEvaluation gate, int maxPages)
    {
        if (_budget is null)
        {
            return maxPages;
        }

        if (!_budget.TryReserve(ResearchBudgetKind.VerificationFetch))
        {
            return 0;
        }

        if (gate.Decision == SearchResultDecision.Ambiguous || gate.IsDirectory)
        {
            return 1;
        }

        var pages = 1;
        while (pages < maxPages && _budget.TryReserve(ResearchBudgetKind.ContactFetch))
        {
            pages++;
        }

        return pages;
    }

    private async Task<SearchResultEvaluation> ResolveAmbiguousAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        WebSearchHit hit,
        PlannedSearchQuery planned,
        SearchResultEvaluation gate,
        CancellationToken cancellationToken)
    {
        if (_searchClassifier is null || _budget is null || !_budget.TryReserve(ResearchBudgetKind.OllamaCall))
        {
            return gate;
        }

        try
        {
            var classified = await _searchClassifier.ClassifySearchResultAsync(
                new AiSearchResultClassificationRequest
                {
                    Title = hit.Title,
                    Snippet = hit.Snippet,
                    Url = hit.Url,
                    Query = planned.Query,
                    Industries = context.Industries,
                    Countries = context.Countries,
                    Locations = context.Locations,
                    ExcludedIndustries = context.ExcludedIndustries
                },
                cancellationToken);
            state.OllamaCallsUsed++;
            if (!classified.IsValid || !Enum.TryParse<SearchResultDecision>(classified.Decision, out var decision))
            {
                return gate;
            }

            return new SearchResultEvaluation
            {
                Decision = decision,
                Confidence = classified.Confidence,
                Reason = classified.Reason,
                IndustryEvidence = classified.IndustryEvidence,
                CountryEvidence = classified.CountryEvidence,
                NeedsVerification = classified.NeedsVerification
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ollama search-result classification failed for {Url}", hit.Url);
            return gate;
        }
    }

    private static SearchResultGateInput BuildGateInput(
        ProspectSourceContext context,
        WebSearchHit hit,
        PlannedSearchQuery planned)
    {
        var spec = ProjectEligibilityPolicy.RequireSpec(context);
        return new SearchResultGateInput
        {
            Title = hit.Title,
            Snippet = hit.Snippet,
            Url = hit.Url,
            Query = planned.Query,
            QueryIndustry = planned.Industry,
            Industries = spec.ApprovedIndustries.Count > 0 ? spec.ApprovedIndustries : context.Industries,
            ExcludedIndustries = spec.ExcludedIndustries.Count > 0 ? spec.ExcludedIndustries : context.ExcludedIndustries,
            ExcludedKeywords = spec.ExcludedKeywords.Count > 0 ? spec.ExcludedKeywords : context.ExcludedKeywords,
            Countries = spec.TargetCountries.Count > 0 ? spec.TargetCountries : context.Countries,
            Locations = spec.Cities.Count > 0 ? spec.Cities : ResearchSearchQueryGenerator.ResolveLocations(context)
        };
    }

    private static string MapDispositionRejection(ResearchCandidateDisposition disposition)
        => disposition switch
        {
            ResearchCandidateDisposition.RejectedWrongIndustry => "WrongIndustry",
            ResearchCandidateDisposition.RejectedWrongCountry => "WrongGeography",
            ResearchCandidateDisposition.RejectedExcludedCategory => "ExcludedCategory",
            ResearchCandidateDisposition.RejectedVendorNotOperator => "VendorNotOperator",
            ResearchCandidateDisposition.RejectedUnrelated => "Irrelevant",
            ResearchCandidateDisposition.RejectedBeforeFetch => "RejectedBeforeFetch",
            _ => disposition.ToString()
        };

    private async Task EnrichCandidatesWithEvidenceAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        IReadOnlyList<ProspectCandidate> crawled,
        WebSearchHit hit,
        Uri hitUri,
        CancellationToken cancellationToken)
    {
        if (_researchIntelligence is null || crawled.Count == 0)
        {
            return;
        }

        if (_budget is null || !_budget.TryReserve(ResearchBudgetKind.OllamaCall))
        {
            return;
        }

        var candidate = crawled[0];
        var pageText = BuildEvidencePageText(candidate, hit);
        if (string.IsNullOrWhiteSpace(pageText) || pageText.Length < 40)
        {
            return;
        }

        try
        {
            var spec = ProjectEligibilityPolicy.RequireSpec(context);
            var extraction = await _researchIntelligence.ExtractBusinessEvidenceAsync(
                new AiEvidenceExtractionRequest
                {
                    CompanyName = candidate.CompanyName,
                    Url = candidate.Website ?? candidate.SourceUrl ?? hit.Url,
                    PageTitle = hit.Title,
                    PageText = pageText,
                    AllowedEmails = CollectCandidateEmails(crawled),
                    AllowedPhones = CollectCandidatePhones(crawled),
                    Industries = spec.ApprovedIndustries.Count > 0 ? spec.ApprovedIndustries : context.Industries,
                    Countries = spec.TargetCountries.Count > 0 ? spec.TargetCountries : context.Countries,
                    ExcludedIndustries = spec.ExcludedIndustries.Count > 0 ? spec.ExcludedIndustries : context.ExcludedIndustries
                },
                cancellationToken);
            state.OllamaCallsUsed++;
            if (!extraction.IsValid)
            {
                return;
            }

            ApplyEvidenceExtraction(candidate, extraction, hitUri);
            await RecordSearchEventAsync(
                context,
                ResearchRunEventType.BusinessEvidenceExtracted,
                extraction.Reason,
                null,
                new
                {
                    url = hit.Url,
                    businessName = extraction.BusinessName,
                    matchesIndustry = extraction.MatchesIndustry,
                    matchesCountry = extraction.MatchesCountry,
                    hasEmail = !string.IsNullOrWhiteSpace(extraction.Email),
                    missingFields = extraction.MissingFields,
                    confidence = extraction.Confidence
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ollama evidence extraction failed for {Url}", hitUri);
        }
    }

    private static void ApplyEvidenceExtraction(
        ProspectCandidate candidate,
        AiEvidenceExtractionResult extraction,
        Uri hitUri)
    {
        if (!string.IsNullOrWhiteSpace(extraction.BusinessName)
            && !CompanyIdentityResolver.IsGenericName(extraction.BusinessName))
        {
            candidate.CompanyName = extraction.BusinessName;
        }

        if (!string.IsNullOrWhiteSpace(extraction.OfficialWebsite)
            && Uri.TryCreate(extraction.OfficialWebsite, UriKind.Absolute, out var official)
            && string.Equals(official.Host, hitUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            candidate.Website = extraction.OfficialWebsite;
        }

        if (!string.IsNullOrWhiteSpace(extraction.Country))
        {
            candidate.Country = extraction.Country;
        }

        if (!string.IsNullOrWhiteSpace(extraction.City))
        {
            candidate.City = extraction.City;
        }

        if (!string.IsNullOrWhiteSpace(extraction.Industry))
        {
            candidate.Industry = extraction.Industry;
        }

        if (!string.IsNullOrWhiteSpace(extraction.Description))
        {
            candidate.CompanyDescription = extraction.Description;
        }

        if (!string.IsNullOrWhiteSpace(extraction.Email))
        {
            candidate.Email = extraction.Email;
        }

        if (!string.IsNullOrWhiteSpace(extraction.Phone))
        {
            candidate.Phone = extraction.Phone;
        }

        if (!string.IsNullOrWhiteSpace(extraction.WhatsApp))
        {
            candidate.WhatsApp = extraction.WhatsApp;
        }

        if (extraction.EvidenceQuotes.Count > 0)
        {
            candidate.ResearchNotes = AppendCandidateNote(
                candidate.ResearchNotes,
                "Evidence: " + string.Join(" | ", extraction.EvidenceQuotes.Take(3)));
        }

        if (extraction.MissingFields.Count > 0)
        {
            candidate.ResearchNotes = AppendCandidateNote(
                candidate.ResearchNotes,
                "Missing: " + string.Join(", ", extraction.MissingFields.Take(5)));
        }
    }

    private static string BuildEvidencePageText(ProspectCandidate candidate, WebSearchHit hit)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(hit.Title))
        {
            parts.Add(hit.Title);
        }

        if (!string.IsNullOrWhiteSpace(hit.Snippet))
        {
            parts.Add(hit.Snippet);
        }

        if (!string.IsNullOrWhiteSpace(candidate.CompanyDescription))
        {
            parts.Add(candidate.CompanyDescription);
        }

        if (!string.IsNullOrWhiteSpace(candidate.ResearchNotes))
        {
            parts.Add(candidate.ResearchNotes);
        }

        return string.Join("\n", parts);
    }

    private static List<string> CollectCandidateEmails(IReadOnlyList<ProspectCandidate> candidates)
    {
        var emails = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.Email) && seen.Add(candidate.Email))
            {
                emails.Add(candidate.Email);
            }
        }

        return emails;
    }

    private static List<string> CollectCandidatePhones(IReadOnlyList<ProspectCandidate> candidates)
    {
        var phones = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.Phone) && seen.Add(candidate.Phone))
            {
                phones.Add(candidate.Phone);
            }

            if (!string.IsNullOrWhiteSpace(candidate.WhatsApp) && seen.Add(candidate.WhatsApp))
            {
                phones.Add(candidate.WhatsApp);
            }
        }

        return phones;
    }

    private static string AppendCandidateNote(string? existing, string note)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return note;
        }

        if (existing.Contains(note, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return existing + " " + note;
    }

    private static List<ProspectCandidate> VerifyCrawledCandidates(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        IReadOnlyList<ProspectCandidate> crawled,
        WebSearchHit hit,
        Uri hitUri,
        bool isDirectory,
        SearchResultEvaluation gate)
    {
        var verified = new List<ProspectCandidate>();
        foreach (var candidate in crawled)
        {
            if (!PassesCandidateVerification(context, candidate, hit, hitUri, isDirectory, gate))
            {
                state.RecordRejection(ResolveCandidateRejection(context, candidate, hit, hitUri, isDirectory));
                continue;
            }

            ApplyVerifiedEvidence(context, candidate, hit, gate);
            verified.Add(candidate);
        }

        return verified;
    }

    private static string ResolveCandidateRejection(
        ProspectSourceContext context,
        ProspectCandidate candidate,
        WebSearchHit hit,
        Uri hitUri,
        bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidate.CompanyName)
            || CompanyIdentityResolver.IsGenericName(candidate.CompanyName)
            || ProspectJunkClassifier.IsJunkOrganization(candidate.CompanyName, candidate.SourceUrl ?? candidate.Website, candidate.CompanyDescription))
        {
            return "Weak company identity";
        }

        if (!isDirectory && !SameHost(candidate, hitUri))
        {
            return "Official website not verified";
        }

        if (isDirectory && SameHost(candidate, hitUri))
        {
            return "Non-target entity type";
        }

        var industry = IndustryEvidenceClassifier.Classify(
            candidate.CompanyName,
            candidate.CompanyName,
            CombineDescription(candidate, hit),
            candidate.Website ?? candidate.SourceUrl,
            context.Industries,
            MergeContextExclusions(context));
        if (!industry.MatchedApprovedIndustry)
        {
            return "Industry mismatch";
        }

        return "Outside target country";
    }

    private static bool PassesCandidateVerification(
        ProspectSourceContext context,
        ProspectCandidate candidate,
        WebSearchHit hit,
        Uri hitUri,
        bool isDirectory,
        SearchResultEvaluation gate)
    {
        if (string.IsNullOrWhiteSpace(candidate.CompanyName)
            || CompanyIdentityResolver.IsGenericName(candidate.CompanyName)
            || ProspectJunkClassifier.IsJunkOrganization(candidate.CompanyName, candidate.SourceUrl ?? candidate.Website, candidate.CompanyDescription))
        {
            return false;
        }

        if (isDirectory && SameHost(candidate, hitUri))
        {
            return false;
        }

        if (!isDirectory && !SameHost(candidate, hitUri))
        {
            return false;
        }

        if (!isDirectory && IsNonBusinessPage(candidate))
        {
            return false;
        }

        var industry = IndustryEvidenceClassifier.Classify(
            candidate.CompanyName,
            candidate.CompanyName,
            CombineDescription(candidate, hit),
            candidate.Website ?? candidate.SourceUrl,
            context.Industries,
            MergeContextExclusions(context));
        if (industry.IsExcluded || !industry.MatchedApprovedIndustry)
        {
            return false;
        }

        candidate.Industry = industry.AssignedIndustry ?? candidate.Industry;
        return HasCountryEvidence(context, candidate, hit, gate);
    }

    private static bool HasCountryEvidence(
        ProspectSourceContext context,
        ProspectCandidate candidate,
        WebSearchHit hit,
        SearchResultEvaluation gate)
    {
        if (context.Countries.Count == 0)
        {
            return true;
        }

        var countryGate = OrganizationCountryGate.Evaluate(candidate, context.Countries);
        if (countryGate.Passed)
        {
            return true;
        }

        if (countryGate.State == OrganizationCountryMatchState.Mismatch)
        {
            return false;
        }

        var combined = $"{candidate.CompanyName} {candidate.CompanyDescription} {candidate.City} {hit.Title} {hit.Snippet}";
        var synthetic = SearchResultGate.Evaluate(new SearchResultGateInput
        {
            Title = candidate.CompanyName,
            Snippet = combined,
            Url = candidate.Website ?? candidate.SourceUrl ?? hit.Url,
            Industries = context.Industries,
            ExcludedIndustries = context.ExcludedIndustries,
            Countries = context.Countries,
            Locations = ResearchSearchQueryGenerator.ResolveLocations(context)
        });
        return synthetic.CountryEvidence is not null || gate.CountryEvidence is not null;
    }

    private static void ApplyVerifiedEvidence(
        ProspectSourceContext context,
        ProspectCandidate candidate,
        WebSearchHit hit,
        SearchResultEvaluation gate)
    {
        if (context.Countries.Count > 0
            && (string.IsNullOrWhiteSpace(candidate.Country)
                || !CountryScopeHelper.CountryMatches(candidate.Country, context.Countries)))
        {
            var countryGate = OrganizationCountryGate.Evaluate(candidate, context.Countries);
            if (countryGate.State != OrganizationCountryMatchState.Mismatch)
            {
                candidate.Country = countryGate.InferredCountry ?? context.Countries[0];
            }
        }

        if (string.IsNullOrWhiteSpace(candidate.CountryEvidenceSummary))
        {
            candidate.CountryEvidenceSummary = gate.CountryEvidence
                ?? $"Search evidence: {hit.Title} {hit.Snippet}".Trim();
        }

        if (string.IsNullOrWhiteSpace(candidate.CountryEvidenceSource))
        {
            candidate.CountryEvidenceSource = "search_result_and_page_verification";
        }
        candidate.SourceType = ProspectSourceType.Search;
        if (!string.IsNullOrWhiteSpace(candidate.Email)
            && !EmailAddressParser.IsAcceptableBusinessEmail(candidate.Email))
        {
            candidate.Email = null;
        }

        candidate.AdditionalEmails = candidate.AdditionalEmails
            .Where(x => EmailAddressParser.IsAcceptableBusinessEmail(x))
            .ToList();
        candidate.AttributedEmails = candidate.AttributedEmails
            .Where(x => EmailAddressParser.IsAcceptableBusinessEmail(x.Value))
            .ToList();
    }

    private static bool IsNonBusinessPage(ProspectCandidate candidate)
    {
        var purpose = UrlAdmissionPolicy.ClassifyPagePurpose(candidate.SourceUrl ?? candidate.Website);
        if (purpose is UrlPagePurpose.AssetOrApi or UrlPagePurpose.Listicle or UrlPagePurpose.JobListing)
        {
            return true;
        }

        var entity = EntityClassifier.Classify(
            candidate.CompanyName,
            candidate.CompanyName,
            candidate.CompanyDescription,
            candidate.Website ?? candidate.SourceUrl);
        return entity.Confidence >= 70
               && entity.EntityType is Domain.Enums.ResearchEntityType.DirectoryAggregator
                or Domain.Enums.ResearchEntityType.PublisherEditorial
                or Domain.Enums.ResearchEntityType.Marketplace
                or Domain.Enums.ResearchEntityType.GovernmentOrInfoPortal;
    }

    private static string CombineDescription(ProspectCandidate candidate, WebSearchHit hit)
    {
        return $"{candidate.CompanyDescription} {candidate.ResearchNotes} {hit.Snippet}".Trim();
    }

    private static bool SameHost(ProspectCandidate candidate, Uri hitUri)
    {
        var url = candidate.Website ?? candidate.SourceUrl;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var candidateUri))
        {
            return false;
        }

        return string.Equals(candidateUri.Host, hitUri.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> MergeContextExclusions(ProspectSourceContext context)
    {
        var result = new List<string>(context.ExcludedIndustries);
        result.AddRange(context.ExcludedKeywords);
        return result;
    }

    private static bool HasMinimumProspectEvidence(ProspectCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.CompanyName)
            || CompanyIdentityResolver.IsGenericName(candidate.CompanyName))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidate.Website)
            && string.IsNullOrWhiteSpace(ProspectNormalizer.NormalizeWebsite(candidate.SourceUrl)))
        {
            return false;
        }

        var website = ProspectNormalizer.NormalizeWebsite(candidate.Website)
                      ?? ProspectNormalizer.NormalizeWebsite(candidate.SourceUrl);
        if (!string.IsNullOrWhiteSpace(website)
            && ResearchDirectoryCatalog.IsDirectoryHost(website)
            && string.IsNullOrWhiteSpace(candidate.Email))
        {
            return false;
        }

        return true;
    }

    private async Task EnsureQueriesAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        if (state.GeneratedQueries.Count > 0 && !state.OllamaChecked)
        {
            return;
        }

        if (state.PlannedSearchQueries.Count > 0 && !state.OllamaChecked)
        {
            state.GeneratedQueries = state.PlannedSearchQueries.Select(x => x.Query).ToList();
            return;
        }

        var maxQueries = Math.Clamp(_options.MaxSearchQueries, 1, 200);
        if (state.OllamaChecked && (state.GeneratedQueries.Count > 0 || state.PlannedSearchQueries.Count > 0))
        {
            MergeDeterministicPlan(context, state, maxQueries);
            _logger.LogInformation("Generated {Count} research search queries (including Ollama)", state.GeneratedQueries.Count);
            return;
        }
        state.PlannedSearchQueries = ResearchSearchQueryGenerator.GeneratePlan(context, maxQueries).ToList();
        state.GeneratedQueries = state.PlannedSearchQueries.Select(x => x.Query).ToList();
        await AppendAiQueriesAsync(context, state, maxQueries, cancellationToken);
        _logger.LogInformation("Generated {Count} research search queries", state.GeneratedQueries.Count);
    }

    private static void MergeDeterministicPlan(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        int maxQueries)
    {
        var deterministic = ResearchSearchQueryGenerator.GeneratePlan(context, maxQueries);
        var seen = new HashSet<string>(state.GeneratedQueries, StringComparer.OrdinalIgnoreCase);
        foreach (var planned in deterministic)
        {
            if (string.IsNullOrWhiteSpace(planned.Query) || !seen.Add(planned.Query.Trim()))
            {
                continue;
            }

            state.GeneratedQueries.Add(planned.Query.Trim());
            state.PlannedSearchQueries.Add(planned);
        }
    }

    private async Task AppendAiQueriesAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        int maxQueries,
        CancellationToken cancellationToken)
    {
        if (_queryPlanner is null || state.GeneratedQueries.Count >= maxQueries)
        {
            return;
        }

        try
        {
            var aiQueries = await _queryPlanner.GenerateSearchQueriesAsync(
                new AiQueryGenerationRequest
                {
                    ProjectName = context.ProjectName,
                    ProjectDescription = context.Description,
                    Industries = context.Industries,
                    Countries = context.Countries,
                    Locations = context.Locations,
                    Keywords = context.Keywords,
                    ExistingQueries = state.GeneratedQueries,
                    MaxQueries = Math.Min(12, maxQueries - state.GeneratedQueries.Count)
                },
                cancellationToken);
            var seen = new HashSet<string>(state.GeneratedQueries, StringComparer.OrdinalIgnoreCase);
            foreach (var query in aiQueries ?? [])
            {
                if (string.IsNullOrWhiteSpace(query) || !seen.Add(query.Trim()))
                {
                    continue;
                }

                state.GeneratedQueries.Add(query.Trim());
                state.PlannedSearchQueries.Add(new PlannedSearchQuery
                {
                    Query = query.Trim(),
                    Industry = context.Industries.FirstOrDefault() ?? "business",
                    Intent = SearchQueryIntents.DiscoverBusiness
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Local AI query generation failed; deterministic queries remain in use");
        }
    }

    private static PlannedSearchQuery? TakeNextPlannedQuery(
        ProspectSourceContext context,
        ResearchDiscoveryState state)
    {
        if (state.PlannedSearchQueries.Count == 0)
        {
            if (state.NextQueryIndex >= state.GeneratedQueries.Count)
            {
                return null;
            }

            var fallback = state.GeneratedQueries[state.NextQueryIndex];
            state.NextQueryIndex++;
            return new PlannedSearchQuery
            {
                Query = fallback,
                Industry = context.Industries.FirstOrDefault() ?? "business",
                Intent = SearchQueryIntents.DiscoverBusiness
            };
        }

        var next = SearchCampaignPlanner.SelectNext(state.PlannedSearchQueries, state);
        if (next is null)
        {
            return null;
        }

        state.ExecutedSearchQueryKeys.Add(SearchCampaignPlanner.BuildKey(next));
        state.NextQueryIndex = Math.Min(state.NextQueryIndex + 1, state.GeneratedQueries.Count);
        return next;
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

        if (HasRemainingPlannedQuery(state) || state.NextQueryIndex < state.GeneratedQueries.Count)
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

    private async Task AppendAdaptiveQueriesAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        if (state.SearchQueriesExecuted == 0)
        {
            return;
        }

        if (_budget is not null && !_budget.CanReserve(ResearchBudgetKind.OllamaCall))
        {
            return;
        }

        if (_adaptivePlanner is not null)
        {
            var beforeCalls = state.OllamaCallsUsed;
            await AppendAdaptivePlanAsync(context, state, cancellationToken);
            if (state.OllamaCallsUsed > beforeCalls)
            {
                _budget?.TryReserve(ResearchBudgetKind.OllamaCall);
            }

            return;
        }

        if (_budget is not null && !_budget.TryReserve(ResearchBudgetKind.OllamaCall))
        {
            return;
        }

        await AppendLegacyAdaptiveQueriesAsync(context, state, cancellationToken);
    }

    private async Task AppendAdaptivePlanAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var remainingSearch = Math.Max(0, ResolveMaxSearchRequests() - (state.SearchRequestsSuccessful + state.SearchRequestsFailed));
            var remainingOllama = _budget?.RemainingFor(ResearchBudgetKind.OllamaCall) ?? Math.Max(0, _options.MaxOllamaCallsPerRun - state.OllamaCallsUsed);
            var catalog = AdaptiveResearchPlanner.BuildDefaultSourceCatalog(
                webSearchAvailable: IsEnabled,
                independentCrawlerAvailable: _options.EnableIndependentCrawler,
                directoryCatalogAvailable: true,
                associationCatalogAvailable: true,
                osmAvailable: true,
                localIndexAvailable: true,
                commonCrawlAvailable: true);
            var planned = await _adaptivePlanner!.PlanAsync(
                context,
                state,
                catalog,
                maxActions: 6,
                isFollowUp: true,
                remainingOllamaCalls: remainingOllama,
                remainingSearchRequests: remainingSearch,
                cancellationToken);
            var seen = new HashSet<string>(state.GeneratedQueries, StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var item in planned)
            {
                var trimmed = item.Query?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
                {
                    continue;
                }

                state.GeneratedQueries.Add(trimmed);
                state.PlannedSearchQueries.Add(item);
                added++;
            }

            if (added > 0 || !string.IsNullOrWhiteSpace(state.LastAdaptiveStrategySummary))
            {
                await RecordSearchEventAsync(
                    context,
                    ResearchRunEventType.ResearchStrategyAdapted,
                    state.LastAdaptiveStrategySummary ?? $"Adaptive plan added {added} actions.",
                    null,
                    new
                    {
                        actionsAdded = added,
                        abandonedPatterns = state.AbandonedSearchPatterns.Take(10).ToList(),
                        adaptiveAccepted = state.AdaptiveActionsAccepted,
                        adaptiveRejected = state.AdaptiveActionsRejected
                    },
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Adaptive research plan failed; deterministic expansion remains available");
            state.RecordRejection("AdaptivePlanFailed");
        }
    }

    private async Task AppendLegacyAdaptiveQueriesAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        if (_queryPlanner is null)
        {
            return;
        }

        try
        {
            var spec = ProjectEligibilityPolicy.RequireSpec(context);
            var queries = await _queryPlanner.GenerateSearchQueriesAsync(
                new AiQueryGenerationRequest
                {
                    ProjectName = context.ProjectName,
                    ProjectDescription = context.Description,
                    Industries = spec.ApprovedIndustries.Count > 0 ? spec.ApprovedIndustries : context.Industries,
                    IndustrySynonyms = spec.IndustrySynonyms,
                    Countries = spec.TargetCountries.Count > 0 ? spec.TargetCountries : context.Countries,
                    Locations = spec.Cities.Count > 0 ? spec.Cities : context.Locations,
                    Keywords = context.Keywords,
                    ExcludedIndustries = spec.ExcludedIndustries,
                    ExcludedKeywords = spec.ExcludedKeywords,
                    ExistingQueries = state.GeneratedQueries,
                    MaxQueries = 8,
                    HardConstraintsSummary =
                        $"Countries={string.Join('|', spec.TargetCountries)}; Industries={string.Join('|', spec.ApprovedIndustries)}; Exclusions={string.Join('|', spec.ExcludedIndustries)}",
                    CoverageSummary = $"queries={state.SearchQueriesExecuted}, results={state.SearchResultsReceived}, candidates={state.CandidatesDiscovered}",
                    RejectionSummary = state.RejectionReasons.Count == 0
                        ? null
                        : string.Join(", ", state.RejectionReasons.Select(x => $"{x.Key}={x.Value}")),
                    UnderCoveredIndustries = state.ResolveUnderCoveredIndustries(),
                    UnderCoveredLocations = context.Locations
                },
                cancellationToken);
            state.OllamaCallsUsed++;
            var seen = new HashSet<string>(state.GeneratedQueries, StringComparer.OrdinalIgnoreCase);
            foreach (var query in queries ?? [])
            {
                var trimmed = query?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 200 || !seen.Add(trimmed))
                {
                    continue;
                }

                var planned = new PlannedSearchQuery
                {
                    Query = trimmed,
                    Industry = context.Industries.FirstOrDefault() ?? string.Empty,
                    Country = context.Countries.FirstOrDefault(),
                    Location = context.Locations.FirstOrDefault(),
                    Intent = SearchQueryIntents.DiscoverBusiness,
                    ProjectId = context.ProjectId,
                    ResearchQueryVersionId = context.QueryVersion?.Id,
                    Purpose = "adaptive_coverage_gap",
                    ExpectedResultType = "operating_business"
                };
                var validation = ProjectEligibilityPolicy.ValidateQuery(
                    planned.Query,
                    spec,
                    planned.Industry,
                    planned.Country);
                if (!validation.IsValid)
                {
                    state.RecordRejection("InvalidAiQuery");
                    state.AdaptiveActionsRejected++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(validation.MatchedIndustry))
                {
                    planned.Industry = validation.MatchedIndustry;
                }

                state.GeneratedQueries.Add(trimmed);
                state.PlannedSearchQueries.Add(planned);
                state.AdaptiveActionsAccepted++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Adaptive Ollama query generation failed; deterministic expansion remains in use");
        }
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
            foreach (var query in extra)
            {
                state.PlannedSearchQueries.Add(new PlannedSearchQuery
                {
                    Query = query,
                    Industry = context.Industries.FirstOrDefault() ?? "business",
                    Intent = SearchQueryIntents.DiscoverBusiness
                });
            }

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
        state.SearchLimitsExhausted = true;
        state.SearchLimitsExhaustedReason ??= reason;

        if (ResearchRunContinuationPolicy.IsRunLevelLimitReason(reason))
        {
            MarkRunLevelLimit(state, reason);
            return;
        }

        if (ResearchRunContinuationPolicy.IsSearchOnlyLimitReason(reason))
        {
            return;
        }

        MarkRunLevelLimit(state, reason);
    }

    private static void MarkRunLevelLimit(ResearchDiscoveryState state, string reason)
    {
        state.LimitsExhausted = true;
        state.LimitsExhaustedReason ??= reason;
    }

    private int ResolveMaxSearchRequests()
    {
        var researchCap = Math.Clamp(_options.MaxSearchRequests, 1, 500);
        var providerBudget = 0;
        if (_braveOptions.Enabled)
        {
            providerBudget += Math.Clamp(_braveOptions.MaxRequestsPerResearchRun, 1, 500);
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
        CompleteWithLimit(state, reason);
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

    private static bool HasRemainingPlannedQuery(ResearchDiscoveryState state)
        => state.PlannedSearchQueries.Count > 0
           && SearchCampaignPlanner.SelectNext(state.PlannedSearchQueries, state) is not null;

    private static void RecordQueryYield(
        ResearchDiscoveryState state,
        PlannedSearchQuery planned,
        List<WebSearchHit> hits,
        ProspectSourceContext context)
    {
        var relevant = 0;
        foreach (var hit in hits)
        {
            var classification = IndustryEvidenceClassifier.Classify(
                hit.Title,
                hit.Title,
                hit.Snippet,
                hit.Url,
                context.Industries,
                context.ExcludedIndustries);
            if (classification.MatchedApprovedIndustry && !classification.IsExcluded)
            {
                relevant++;
            }
        }

        if (relevant > 0)
        {
            Increment(state.RelevantHitsByIndustry, planned.Industry);
            return;
        }

        if (hits.Count >= 3)
        {
            state.LowYieldQueryKeys.Add(SearchCampaignPlanner.BuildYieldKey(planned));
        }
    }

    private async Task PersistSearchHitsAsync(
        ProspectSourceContext context,
        PlannedSearchQuery planned,
        List<WebSearchHit> hits,
        CancellationToken cancellationToken)
    {
        if (_db is null || hits.Count == 0)
        {
            return;
        }

        foreach (var hit in hits.Take(20))
        {
            if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            var admission = UrlAdmissionPolicy.Evaluate(
                uri.ToString(),
                countries: context.Countries,
                industries: context.Industries);
            if (!admission.Accepted)
            {
                context.DiscoveryState.RecordUrlRejection(admission.ReasonCode);
                continue;
            }

            var canonical = admission.NormalizedUrl ?? UrlCanonicalizer.Canonicalize(uri.ToString());
            if (string.IsNullOrWhiteSpace(canonical))
            {
                continue;
            }

            var exists = await _db.DiscoveredUrls.AnyAsync(
                x => x.ResearchRunId == context.ResearchRunId && x.NormalizedUrl == canonical,
                cancellationToken);
            if (exists)
            {
                continue;
            }

            _db.DiscoveredUrls.Add(new DiscoveredUrl
            {
                ResearchRunId = context.ResearchRunId,
                ProjectId = context.ProjectId,
                Url = uri.ToString(),
                NormalizedUrl = canonical,
                NormalizedDomain = UrlCanonicalizer.ExtractDomain(canonical),
                SourceType = DiscoverySourceType.WebSearch,
                SourceName = hit.Provider ?? "Web search",
                Status = DiscoveredUrlStatus.Pending,
                Depth = 0,
                DiscoveryReason = planned.Intent,
                AssociatedIndustry = planned.Industry,
                AssociatedLocation = planned.Location,
                SchedulingReason = "search_hit",
                ExpectedDiscoveryValue = 40
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void Increment(Dictionary<string, int> counts, string? key)
    {
        var name = string.IsNullOrWhiteSpace(key) ? "unknown" : key;
        counts[name] = counts.TryGetValue(name, out var current) ? current + 1 : 1;
    }

    private async Task<bool> EnsureSearchProviderHealthyAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        if (state.SearchHealthChecked)
        {
            return state.SearchProviderHealth is SearchProviderHealthState.Healthy
                or SearchProviderHealthState.Degraded
                or SearchProviderHealthState.NoResults
                or SearchProviderHealthState.ResultsFiltered;
        }

        state.SearchHealthChecked = true;
        WebSearchOutcome? health = null;
        var anyUsable = false;
        foreach (var provider in _searchProviders)
        {
            if (!provider.IsEnabled || provider is not IDiagnosticWebSearchProvider diagnostic)
            {
                if (provider.IsEnabled)
                {
                    anyUsable = true;
                }

                continue;
            }

            health = await diagnostic.HealthCheckAsync(cancellationToken);
            state.SearchProviderHealth = CombineStates(state.SearchProviderHealth, health.State);
            if (!string.IsNullOrWhiteSpace(health.ErrorSummary)
                && state.SearchProviderHealth is SearchProviderHealthState.Unavailable
                    or SearchProviderHealthState.InvalidResponse)
            {
                state.SearchProviderHealthSummary = health.ErrorSummary;
            }

            await RecordSearchEventAsync(
                context,
                ResearchRunEventType.SearchProviderHealthChecked,
                $"{provider.Name} health: {health.State}",
                null,
                new
                {
                    provider = provider.Name,
                    state = health.State.ToString(),
                    error = health.ErrorSummary,
                    unresponsiveEngines = health.UnresponsiveEngines,
                    resultCount = health.ProviderResultCount
                },
                cancellationToken,
                provider.Name);
            if (health.State is SearchProviderHealthState.Healthy
                or SearchProviderHealthState.Degraded
                or SearchProviderHealthState.NoResults
                or SearchProviderHealthState.ResultsFiltered)
            {
                anyUsable = true;
                state.SearchProviderHealth = health.State;
                state.SearchProviderHealthSummary = health.ErrorSummary;
                return true;
            }
        }

        if (anyUsable || health is null)
        {
            return true;
        }

        CompleteWithLimit(state, "Search provider unavailable");
        state.CurrentInvestigationActivity = "search_exhausted_fallback_to_other_sources";
        await RecordSearchEventAsync(
            context,
            ResearchRunEventType.SearchProviderAttempted,
            $"HTML search unavailable ({health.ErrorSummary}). Continuing with directories, local index, OpenStreetMap, and other free sources.",
            null,
            new
            {
                state = health.State.ToString(),
                error = health.ErrorSummary,
                fallback = new[]
                {
                    "DirectoryCatalog",
                    "LocalBusinessIndex",
                    "OpenStreetMapNominatim",
                    "CommonCrawlIndex",
                    "SeedUrls",
                    "PriorResearchUrls"
                }
            },
            cancellationToken);
        return false;
    }

    private async Task RecordQueryCompletionAsync(
        ProspectSourceContext context,
        PlannedSearchQuery planned,
        List<WebSearchHit> hits,
        SearchProviderHealthState state,
        string? errorSummary,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var duration = (int)Math.Max(0, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        var eventType = state is SearchProviderHealthState.Unavailable or SearchProviderHealthState.InvalidResponse
            ? ResearchRunEventType.SearchQueryFailed
            : hits.Count == 0
                ? ResearchRunEventType.SearchQueryNoResults
                : ResearchRunEventType.SearchQueryCompleted;
        var message = eventType switch
        {
            ResearchRunEventType.SearchQueryFailed => $"Search query failed: {planned.Query}",
            ResearchRunEventType.SearchQueryNoResults => $"Search query returned zero results: {planned.Query}",
            _ => $"Search query completed: {planned.Query} ({hits.Count} hits)"
        };
        await RecordSearchEventAsync(
            context,
            eventType,
            message,
            planned,
            new
            {
                providerResultCount = hits.Count,
                acceptedCount = hits.Count,
                state = state.ToString(),
                error = errorSummary
            },
            cancellationToken,
            durationMs: duration);
    }

    private async Task RecordSearchEventAsync(
        ProspectSourceContext context,
        ResearchRunEventType eventType,
        string message,
        PlannedSearchQuery? planned,
        object? data,
        CancellationToken cancellationToken,
        string? provider = null,
        int? durationMs = null)
    {
        if (_events is null || context.ResearchRunId == Guid.Empty)
        {
            return;
        }

        try
        {
            await _events.RecordAsync(
                new ResearchEventRecordRequest
                {
                    ResearchRunId = context.ResearchRunId,
                    EventType = eventType,
                    Severity = eventType is ResearchRunEventType.SearchQueryFailed
                        or ResearchRunEventType.ResearchBlocked
                        ? ResearchRunEventSeverity.Warning
                        : ResearchRunEventSeverity.Info,
                    Stage = ResearchPipelineStage.Discovery,
                    Message = message,
                    Data = data is null
                        ? new
                        {
                            query = planned?.Query,
                            industry = planned?.Industry,
                            location = planned?.Location,
                            provider
                        }
                        : data,
                    DurationMs = durationMs,
                    Provider = provider
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to record search event {EventType}", eventType);
        }
    }
}
