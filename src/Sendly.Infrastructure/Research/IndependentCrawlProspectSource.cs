using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Application.Services.WebsiteInvestigation;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Sendly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class IndependentCrawlProspectSource : IProspectSource
{
    private readonly ISendlyDbContext _db;
    private readonly IAutomaticDiscoveryCoordinator _discoveryCoordinator;
    private readonly ISafeHttpFetcher _fetcher;
    private readonly IRobotsTxtService _robots;
    private readonly ISitemapParser _sitemapParser;
    private readonly DiscoverySourceExpansionService _sourceExpansion;
    private readonly DiscoverySourceRegistryService _sourceRegistry;
    private readonly IEnumerable<IUrlDiscoverySource> _discoverySources;
    private readonly IResearchEventRecorder _events;
    private readonly ResearchOptions _options;
    private readonly ILogger<IndependentCrawlProspectSource> _logger;

    public IndependentCrawlProspectSource(
        ISendlyDbContext db,
        IAutomaticDiscoveryCoordinator discoveryCoordinator,
        ISafeHttpFetcher fetcher,
        IRobotsTxtService robots,
        ISitemapParser sitemapParser,
        DiscoverySourceExpansionService sourceExpansion,
        DiscoverySourceRegistryService sourceRegistry,
        IEnumerable<IUrlDiscoverySource> discoverySources,
        IResearchEventRecorder events,
        IOptions<ResearchOptions> options,
        ILogger<IndependentCrawlProspectSource> logger)
    {
        _db = db;
        _discoveryCoordinator = discoveryCoordinator;
        _fetcher = fetcher;
        _robots = robots;
        _sitemapParser = sitemapParser;
        _sourceExpansion = sourceExpansion;
        _sourceRegistry = sourceRegistry;
        _discoverySources = discoverySources;
        _events = events;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "IndependentCrawler";

    public bool IsEnabled => _options.EnableIndependentCrawler;

    public async Task<IReadOnlyList<ProspectCandidate>> DiscoverAsync(
        ProspectSourceContext context,
        CancellationToken cancellationToken)
    {
        if (context.DiscoveryState.CompletedSourceNames.Contains(Name))
        {
            return [];
        }

        var run = await _db.ResearchRuns.FirstOrDefaultAsync(x => x.Id == context.ResearchRunId, cancellationToken);
        if (run is null)
        {
            return [];
        }

        var version = run.ResearchQueryVersionId is null
            ? null
            : await _db.ResearchQueryVersions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == run.ResearchQueryVersionId.Value, cancellationToken);

        var discoveryContext = BuildDiscoveryContext(context, run, version);
        var budget = DiscoveryBudgetResolver.Resolve(_options, version);
        context.DiscoveryState.ActiveProviderName = Name;
        context.DiscoveryState.FetchBudgetLimit = budget.MaxFetchesPerRun;
        context.DiscoveryState.DiscoveryBudgetLimit = budget.MaxDiscoveryCandidates;
        context.DiscoveryState.EffectiveMaxLinksPerPage = budget.MaxLinksPerPage;
        context.DiscoveryState.EffectiveMaxLinksPerDomain = budget.MaxLinksPerDomain;
        context.DiscoveryState.EffectiveSearchReservePercent = budget.SearchFetchBudgetReservePercent;

        if (!context.DiscoveryState.IndependentDiscoverySeeded)
        {
            var discoveryResult = await _discoveryCoordinator.DiscoverAsync(discoveryContext, cancellationToken);
            context.DiscoveryState.IndependentDiscoverySeeded = true;
            context.DiscoveryState.AutomaticDiscoverySummary = discoveryResult.Summary;
            context.DiscoveryState.AutomaticDiscoveryProviderStats = discoveryResult.ProviderStats;
            context.DiscoveryState.AutomaticDiscoveryDuplicatesRemoved = discoveryResult.DuplicatesRemoved;
            context.DiscoveryState.AutomaticDiscoveryRejectedUnsafe = discoveryResult.RejectedUnsafe;
            context.DiscoveryState.UrlsRejectedFiltered = discoveryResult.RejectedFiltered;
            foreach (var pair in discoveryResult.UrlRejectionReasons)
            {
                context.DiscoveryState.UrlRejectionReasons[pair.Key] = pair.Value;
            }

            context.DiscoveryState.DiscoveryPlanSummary = discoveryResult.Plan?.Summary;
            context.DiscoveryState.DiscoveryStages = discoveryResult.Stages;
            context.DiscoveryState.UniqueDomainsDiscovered = discoveryResult.UniqueDomains;
            context.DiscoveryState.DiscoveryStopReason = discoveryResult.StopReason;
            context.DiscoveryState.RecoveryAttempts = discoveryResult.RecoveryActions.Count;
            context.DiscoveryState.RecoveryStrategies = discoveryResult.RecoveryActions;
            context.DiscoveryState.RecoveryTelemetry = discoveryResult.RecoveryTelemetry;
            context.DiscoveryState.SourcesReused = discoveryResult.SourcesReused;
            context.DiscoveryState.SourcesAdded = discoveryResult.SourcesAdded;
            context.DiscoveryState.SourcesSkipped = discoveryResult.SourcesSkipped;
            context.DiscoveryState.SourcesUnavailable = discoveryResult.SourcesUnavailable;
            context.DiscoveryState.EnableContactPageHints = discoveryContext.EnableContactPageHints;
            context.DiscoveryState.DiscoveryBudgetConsumed = discoveryResult.Candidates.Count;
            context.DiscoveryState.SourceEligibilityDecisions = discoveryResult.SourceEligibilityDecisions;
            context.DiscoveryState.RelevantIndustrySourcesAdmitted = discoveryResult.RelevantIndustrySourcesAdmitted;
            context.DiscoveryState.SecondaryRegistrySourcesAdmitted = discoveryResult.SecondaryRegistrySourcesAdmitted;

            await RecordSourceEligibilityEventsAsync(run.Id, discoveryResult, cancellationToken);

            if (context.ReportProgressAsync is not null)
            {
                await context.ReportProgressAsync(context.DiscoveryState, cancellationToken);
            }

            if (discoveryResult.Candidates.Count == 0)
            {
                var stopReason = discoveryResult.StopReason == DiscoveryStopReason.None
                    ? DiscoveryStopReason.NoCandidatesDiscovered
                    : discoveryResult.StopReason;
                context.DiscoveryState.DiscoveryStopReason = stopReason;
                var description = DiscoveryOutcomeClassifier.Describe(stopReason);
                context.DiscoveryState.LastBudgetStopReason =
                    stopReason == DiscoveryStopReason.NoRelevantDiscoverySources
                        ? "discovery.no_relevant_sources"
                        : "discovery.no_candidates";
                run.Warning = description;
                var catalogGapOnly = stopReason is DiscoveryStopReason.NoRelevantDiscoverySources
                    or DiscoveryStopReason.NoCandidatesDiscovered;
                if (!catalogGapOnly)
                {
                    context.DiscoveryState.LimitsExhausted = true;
                    context.DiscoveryState.LimitsExhaustedReason = description;
                }
                else
                {
                    context.DiscoveryState.LimitsExhaustedReason = description;
                    context.DiscoveryState.MoreEligibleWorkRemained = false;
                }

                await RecordEventAsync(
                    run.Id,
                    ResearchRunEventType.DiscoveryCoverageDecision,
                    ResearchRunEventSeverity.Warning,
                    ResearchPipelineStage.Discovery,
                    description,
                    provider: Name,
                    data: new
                    {
                        reasonCode = context.DiscoveryState.LastBudgetStopReason,
                        stopReason = stopReason.ToString(),
                        allowAlternateProviders = catalogGapOnly,
                        relevantIndustrySources = discoveryResult.RelevantIndustrySourcesAdmitted,
                        secondaryRegistries = discoveryResult.SecondaryRegistrySourcesAdmitted,
                        decisions = discoveryResult.SourceEligibilityDecisions.Select(x => new
                        {
                            x.SourceName,
                            x.SourceUrl,
                            state = x.State.ToString(),
                            x.ReasonCode,
                            x.Explanation,
                            x.CountsAsRelevantIndustrySource
                        }).ToList()
                    },
                    cancellationToken: cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                return [];
            }

            await PersistDiscoveredUrlsAsync(
                run,
                discoveryResult.Candidates,
                cancellationToken,
                discoveryContext.Industries,
                discoveryContext.Countries,
                context.DiscoveryState);
        }

        var maxPagesPerDomain = budget.MaxPagesPerDomain;
        var maxDepth = budget.MaxCrawlDepth;
        var timeoutSeconds = budget.RequestTimeoutSeconds;
        var enableSitemap = version?.EnableSitemapDiscovery ?? true;
        var enableLinkExpansion = version?.EnableLinkExpansion ?? true;

        var pendingAll = await _db.DiscoveredUrls
            .Where(x => x.ResearchRunId == run.Id && x.Status == DiscoveredUrlStatus.Pending)
            .ToListAsync(cancellationToken);
        await RejectUnsafePendingUrlsAsync(context, run, pendingAll, cancellationToken);
        pendingAll = pendingAll.Where(x => x.Status == DiscoveredUrlStatus.Pending).ToList();
        context.DiscoveryState.PagesQueued = pendingAll.Count;
        context.DiscoveryState.UsefulPendingUrls = CrawlQueueScheduler.CountUsefulPending(pendingAll);

        var pagesPerDomain = (await _db.CrawledPages
                .Where(x => x.ResearchRunId == run.Id && x.NormalizedDomain != null)
                .GroupBy(x => x.NormalizedDomain!)
                .Select(g => new { Domain = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Domain, x => x.Count, StringComparer.OrdinalIgnoreCase);

        var failedDomains = await _db.DiscoveredUrls
            .Where(x => x.ResearchRunId == run.Id && x.Status == DiscoveredUrlStatus.Failed && x.NormalizedDomain != null)
            .Select(x => x.NormalizedDomain!)
            .Distinct()
            .ToListAsync(cancellationToken);
        var failedDomainSet = new HashSet<string>(failedDomains, StringComparer.OrdinalIgnoreCase);

        var knownProspects = KnownProspectReuseEvaluator.IndexProspects(
            await _db.Prospects.AsNoTracking()
                .Where(x => x.ProjectId == run.ProjectId)
                .ToListAsync(cancellationToken));

        var investigationBonuses = BuildInvestigationHostBonuses(context.DiscoveryState);
        var hostFetchLimits = CrawlQueueScheduler.BuildHostFetchLimits(
            pendingAll,
            maxPagesPerDomain,
            context.DiscoveryState.StrongOrganizationHosts,
            investigationBonuses);
        foreach (var pair in hostFetchLimits)
        {
            context.DiscoveryState.HostFetchAllowances[pair.Key] = pair.Value;
        }

        var maxPerDomainInBatch = ResolveMaxPerDomainInBatch(maxPagesPerDomain, context.DiscoveryState);
        var scheduled = CrawlQueueScheduler.RankPending(
            pendingAll,
            discoveryContext.Industries,
            discoveryContext.Countries,
            pagesPerDomain,
            failedDomainSet,
            maxPagesPerDomain,
            maxPerDomainInBatch,
            hostFetchLimits,
            context.DiscoveryState.StrongOrganizationHosts);

        var relevantScheduled = ResearchRunContinuationPolicy.CountRelevantScheduledWork(scheduled);
        if (!ResearchRunContinuationPolicy.ShouldDeferYieldRecoveryForQueuedWork(
                context.DiscoveryState,
                scheduled.Count,
                relevantScheduled))
        {
            var recoveryStartedAt = DateTimeOffset.UtcNow;
            await MaybeRunYieldRecoveryAsync(context, discoveryContext, run, budget, cancellationToken);
            context.DiscoveryState.RecordStageDuration(
                "yieldRecovery",
                (long)(DateTimeOffset.UtcNow - recoveryStartedAt).TotalMilliseconds);

            pendingAll = await _db.DiscoveredUrls
                .Where(x => x.ResearchRunId == run.Id && x.Status == DiscoveredUrlStatus.Pending)
                .ToListAsync(cancellationToken);
            await RejectUnsafePendingUrlsAsync(context, run, pendingAll, cancellationToken);
            pendingAll = pendingAll.Where(x => x.Status == DiscoveredUrlStatus.Pending).ToList();
            context.DiscoveryState.PagesQueued = pendingAll.Count;
            context.DiscoveryState.UsefulPendingUrls = CrawlQueueScheduler.CountUsefulPending(pendingAll);
            investigationBonuses = BuildInvestigationHostBonuses(context.DiscoveryState);
            hostFetchLimits = CrawlQueueScheduler.BuildHostFetchLimits(
                pendingAll,
                maxPagesPerDomain,
                context.DiscoveryState.StrongOrganizationHosts,
                investigationBonuses);
            foreach (var pair in hostFetchLimits)
            {
                context.DiscoveryState.HostFetchAllowances[pair.Key] = pair.Value;
            }

            maxPerDomainInBatch = ResolveMaxPerDomainInBatch(maxPagesPerDomain, context.DiscoveryState);
            scheduled = CrawlQueueScheduler.RankPending(
                pendingAll,
                discoveryContext.Industries,
                discoveryContext.Countries,
                pagesPerDomain,
                failedDomainSet,
                maxPagesPerDomain,
                maxPerDomainInBatch,
                hostFetchLimits,
                context.DiscoveryState.StrongOrganizationHosts);
            relevantScheduled = ResearchRunContinuationPolicy.CountRelevantScheduledWork(scheduled);
        }
        else if (!context.DiscoveryState.YieldRecoveryAttempted
                 && context.DiscoveryState.PagesFetched >= 8
                 && relevantScheduled > 0)
        {
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.Warning,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.Queue,
                "Deferred yield recovery because relevant queued URLs remain to fetch.",
                provider: Name,
                data: new
                {
                    reasonCode = "recovery.deferred_for_queued_work",
                    eligibleScheduled = scheduled.Count,
                    relevantScheduled,
                    usefulPending = context.DiscoveryState.UsefulPendingUrls,
                    pagesFetched = context.DiscoveryState.PagesFetched,
                    topUrl = scheduled.Count > 0 ? scheduled[0].Url.Url : null,
                    topPriority = scheduled.Count > 0 ? scheduled[0].Priority.Total : 0
                },
                cancellationToken: cancellationToken);
        }

        CaptureTopPendingDiagnostics(context.DiscoveryState, scheduled);
        var maxFetchesThisBatchPreview = Math.Max(context.DiscoveryBatchSize, 8);
        context.DiscoveryState.DeferredHighScoreUrls = CrawlQueueScheduler.ExplainDeferredHighScoreUrls(
            pendingAll,
            scheduled,
            maxFetchesThisBatchPreview,
            pagesPerDomain,
            maxPagesPerDomain,
            hostFetchLimits: hostFetchLimits,
            countries: discoveryContext.Countries);
        context.DiscoveryState.EligiblePendingUrls = scheduled.Count;
        context.DiscoveryState.DeferredUrls = Math.Max(0, pendingAll.Count - scheduled.Count);
        context.DiscoveryState.ConfiguredBatchSize = Math.Max(context.DiscoveryBatchSize, 8);
        context.DiscoveryState.RequestedFetchBudget = budget.MaxFetchesPerRun;
        context.DiscoveryState.BudgetReserved = Math.Min(
            context.DiscoveryState.ConfiguredBatchSize,
            Math.Max(0, budget.MaxFetchesPerRun - context.DiscoveryState.PagesFetched));
        context.DiscoveryState.BudgetRemaining = Math.Max(
            0,
            budget.MaxFetchesPerRun - context.DiscoveryState.FetchBudgetConsumed);

        var pending = scheduled
            .Take(Math.Max(budget.MaxUrlsPerRun, budget.MaxFetchesPerRun))
            .Select(x => x.Url)
            .ToList();

        var candidates = new List<ProspectCandidate>();
        var userAgent = string.IsNullOrWhiteSpace(_options.CrawlerUserAgent)
            ? "SendlyResearchBot/1.0"
            : _options.CrawlerUserAgent;
        var fetchesThisBatch = 0;
        var maxFetchesThisBatch = Math.Max(context.DiscoveryBatchSize, 8);
        var maxHostFailures = Math.Clamp(_options.MaxHostFetchFailuresBeforeAvoid, 1, 10);

        if (pending.Count == 0)
        {
            await CompleteWhenNoEligiblePendingAsync(
                context,
                run,
                budget,
                pendingAll.Count,
                cancellationToken);
            return candidates;
        }

        foreach (var discovered in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidates.Count >= context.DiscoveryBatchSize)
            {
                break;
            }

            if (context.DiscoveryState.PagesFetched >= budget.MaxFetchesPerRun)
            {
                context.DiscoveryState.LastBudgetStopReason = "fetch.run_budget_exhausted";
                context.DiscoveryState.UsefulUrlsLeftPendingAtStop =
                    CrawlQueueScheduler.CountUsefulPending(
                        pendingAll.Where(x => x.Status == DiscoveredUrlStatus.Pending));
                context.DiscoveryState.MoreEligibleWorkRemained =
                    context.DiscoveryState.UsefulUrlsLeftPendingAtStop > 0;
                break;
            }

            if (fetchesThisBatch >= maxFetchesThisBatch)
            {
                context.DiscoveryState.LastBudgetStopReason = "fetch.batch_limit";
                context.DiscoveryState.UsefulUrlsLeftPendingAtStop =
                    CrawlQueueScheduler.CountUsefulPending(
                        pendingAll.Where(x => x.Status == DiscoveredUrlStatus.Pending));
                context.DiscoveryState.MoreEligibleWorkRemained =
                    context.DiscoveryState.UsefulUrlsLeftPendingAtStop > 0;
                break;
            }

            if (IsDurationExceeded(context.DiscoveryState, budget.MaxResearchDurationMinutes))
            {
                context.DiscoveryState.LimitsExhausted = true;
                context.DiscoveryState.LimitsExhaustedReason = "Maximum research duration reached";
                context.DiscoveryState.LastBudgetStopReason = "fetch.duration_exceeded";
                context.DiscoveryState.UsefulUrlsLeftPendingAtStop =
                    CrawlQueueScheduler.CountUsefulPending(
                        pendingAll.Where(x => x.Status == DiscoveredUrlStatus.Pending));
                break;
            }

            var searchReserveActive = !context.DiscoveryState.CompletedSourceNames.Contains("WebSearch")
                                           && !context.DiscoveryState.SearchLimitsExhausted
                                           && (context.DiscoveryState.PlannedSearchQueries.Count > 0
                                               || context.DiscoveryState.GeneratedQueries.Count > 0
                                               || context.DiscoveryState.SearchQueriesExecuted > 0);
            var linkFetchCap = ResearchRunContinuationPolicy.ResolveLinkExpansionFetchCap(
                context.DiscoveryState.PagesFetched,
                budget.MaxFetchesPerRun,
                budget.SearchFetchBudgetReservePercent,
                searchReserveActive);
            if (discovered.SourceType == DiscoverySourceType.LinkExpansion
                && context.DiscoveryState.PagesFetched >= linkFetchCap
                && searchReserveActive)
            {
                context.DiscoveryState.LastBudgetStopReason = "fetch.search_reserve";
                continue;
            }

            var preAdmission = UrlAdmissionPolicy.Evaluate(
                discovered.Url,
                countries: discoveryContext.Countries,
                industries: discoveryContext.Industries);
            if (!preAdmission.Accepted)
            {
                discovered.Status = DiscoveredUrlStatus.SkippedFiltered;
                discovered.RejectionReasonCode = preAdmission.ReasonCode;
                discovered.PagePurpose = preAdmission.PagePurpose.ToString();
                discovered.LastError = preAdmission.Explanation;
                discovered.ProcessedAtUtc = DateTimeOffset.UtcNow;
                context.DiscoveryState.PagesSkipped++;
                context.DiscoveryState.FetchesSkipped++;
                context.DiscoveryState.RecordUrlRejection(preAdmission.ReasonCode);
                await RecordEventAsync(
                    run.Id,
                    ResearchRunEventType.UrlRejected,
                    ResearchRunEventSeverity.Info,
                    ResearchPipelineStage.Discovery,
                    preAdmission.Explanation,
                    url: discovered.Url,
                    domain: discovered.NormalizedDomain,
                    provider: Name,
                    data: new { reasonCode = preAdmission.ReasonCode, pagePurpose = preAdmission.PagePurpose.ToString() },
                    cancellationToken: cancellationToken);
                continue;
            }

            context.DiscoveryState.UrlsAdmitted++;
            var domain = discovered.NormalizedDomain ?? UrlCanonicalizer.ExtractDomain(discovered.Url) ?? "unknown";
            if (context.DiscoveryState.HostFailureCounts.TryGetValue(domain, out var hostFailures)
                && hostFailures >= maxHostFailures)
            {
                discovered.Status = DiscoveredUrlStatus.SkippedBudget;
                discovered.RejectionReasonCode = "fetch.host_avoided";
                discovered.LastError = "Host previously failed repeatedly; remaining URLs deferred.";
                discovered.ProcessedAtUtc = DateTimeOffset.UtcNow;
                context.DiscoveryState.PagesSkipped++;
                context.DiscoveryState.FetchesSkipped++;
                context.DiscoveryState.DeferredUrls++;
                await RecordEventAsync(
                    run.Id,
                    ResearchRunEventType.Warning,
                    ResearchRunEventSeverity.Warning,
                    ResearchPipelineStage.Fetch,
                    discovered.LastError,
                    url: discovered.Url,
                    domain: domain,
                    provider: Name,
                    data: new { reasonCode = "fetch.host_avoided", failures = hostFailures },
                    cancellationToken: cancellationToken);
                continue;
            }

            var reuse = KnownProspectReuseEvaluator.Evaluate(
                discovered.Url,
                knownProspects,
                DateTimeOffset.UtcNow,
                _options.EvidenceReuseMaxAgeDays);
            if (reuse.SkipFetch)
            {
                discovered.Status = DiscoveredUrlStatus.SkippedDuplicate;
                discovered.RejectionReasonCode = reuse.ReasonCode;
                discovered.LastError = reuse.Explanation;
                discovered.ProcessedAtUtc = DateTimeOffset.UtcNow;
                context.DiscoveryState.PagesSkipped++;
                context.DiscoveryState.FetchesSkipped++;
                context.DiscoveryState.UrlsReusedFromCache++;
                context.DiscoveryState.UrlsSkippedKnownProspect++;
                context.DiscoveryState.PreviouslyKnownProspects++;
                if (reuse.ProspectStatus == ProspectStatus.Contacted)
                {
                    context.DiscoveryState.PreviouslyContactedProspects++;
                }

                await RecordEventAsync(
                    run.Id,
                    ResearchRunEventType.DuplicateSkipped,
                    ResearchRunEventSeverity.Info,
                    ResearchPipelineStage.Fetch,
                    reuse.Explanation,
                    url: discovered.Url,
                    domain: domain,
                    provider: Name,
                    data: new
                    {
                        reasonCode = reuse.ReasonCode,
                        prospectId = reuse.ProspectId,
                        status = reuse.ProspectStatus?.ToString(),
                        reuseEvidence = reuse.ReuseEvidence
                    },
                    cancellationToken: cancellationToken);
                continue;
            }

            var investigationBonus = context.DiscoveryState.DomainInvestigationPlans.TryGetValue(domain, out var plan)
                ? WebsiteInvestigationEngine.ResolveAdaptiveHostBonus(plan)
                : 0;
            var hostLimit = hostFetchLimits.TryGetValue(domain, out var configuredLimit)
                ? configuredLimit
                : CrawlQueueScheduler.ResolveHostFetchLimit(
                    maxPagesPerDomain,
                    context.DiscoveryState.StrongOrganizationHosts.Contains(domain),
                    hasUsefulPendingPages: true,
                    investigationBonus);
            if (pagesPerDomain.TryGetValue(domain, out var count) && count >= hostLimit)
            {
                discovered.Status = DiscoveredUrlStatus.SkippedBudget;
                discovered.RejectionReasonCode = "budget.per_domain";
                discovered.LastError = $"Per-domain page limit reached ({count}/{hostLimit}).";
                discovered.ProcessedAtUtc = DateTimeOffset.UtcNow;
                context.DiscoveryState.PagesSkipped++;
                context.DiscoveryState.FetchesSkipped++;
                context.DiscoveryState.DeferredUrls++;
                continue;
            }

            discovered.Status = DiscoveredUrlStatus.Crawling;
            discovered.PagePurpose = preAdmission.PagePurpose.ToString();
            await _db.SaveChangesAsync(cancellationToken);

            if (!Uri.TryCreate(discovered.Url, UriKind.Absolute, out var uri))
            {
                MarkFailed(discovered, "Invalid URL.");
                run.CrawlErrors++;
                context.DiscoveryState.FetchesFailed++;
                await RecordEventAsync(
                    run.Id,
                    ResearchRunEventType.FetchFailed,
                    ResearchRunEventSeverity.Error,
                    ResearchPipelineStage.Fetch,
                    "Invalid URL.",
                    url: discovered.Url,
                    domain: discovered.NormalizedDomain,
                    provider: Name,
                    cancellationToken: cancellationToken);
                continue;
            }

            context.DiscoveryState.FetchesAttempted++;
            context.DiscoveryState.BudgetReserved = Math.Max(
                0,
                context.DiscoveryState.BudgetReserved - 1);
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.FetchStarted,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.Fetch,
                $"Fetching {discovered.Url}",
                url: discovered.Url,
                domain: discovered.NormalizedDomain,
                relatedEntityId: discovered.Id,
                relatedEntityKind: "discoveredUrl",
                provider: Name,
                cancellationToken: cancellationToken);

            var fetch = await _fetcher.FetchAsync(
                new SafeHttpFetchRequest
                {
                    Url = uri,
                    MaxRedirects = _options.MaxRedirects,
                    MaxResponseBytes = _options.MaxResponseBytes,
                    Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 120))
                },
                cancellationToken);

            if (fetch.WasRedirected)
            {
                await RecordEventAsync(
                    run.Id,
                    ResearchRunEventType.RedirectFollowed,
                    ResearchRunEventSeverity.Info,
                    ResearchPipelineStage.Fetch,
                    $"Followed {fetch.RedirectCount} redirect(s) to {fetch.FinalUrl}",
                    url: discovered.Url,
                    domain: discovered.NormalizedDomain,
                    durationMs: fetch.DurationMs,
                    httpStatusCode: fetch.StatusCode,
                    data: new { redirectCount = fetch.RedirectCount, finalUrl = fetch.FinalUrl?.ToString() },
                    provider: Name,
                    cancellationToken: cancellationToken);
            }

            if (!fetch.Succeeded || string.IsNullOrWhiteSpace(fetch.Body) || fetch.BlockedByRobots || fetch.BlockedBySsrf)
            {
                var failure = FetchFailureClassifier.Classify(fetch);
                RecordFetchFailure(context.DiscoveryState, domain, failure);
                context.DiscoveryState.FetchesFailed++;
                context.DiscoveryState.CurrentInvestigationActivity = failure.RetryAppropriate
                    ? "waiting_for_retry"
                    : "blocked_by_limitation";
                if (failure.Kind == FetchFailureKind.RobotsDisallowed)
                {
                    discovered.Status = DiscoveredUrlStatus.SkippedRobots;
                    discovered.LiveVerificationStatus = LiveVerificationStatus.RobotsBlocked;
                    context.DiscoveryState.UrlsRobotsBlocked++;
                }
                else
                {
                    MarkFailed(discovered, failure.Explanation);
                    discovered.LiveVerificationStatus = LiveVerificationStatus.Unavailable;
                    run.CrawlErrors++;
                    context.DiscoveryState.UrlsUnavailable++;
                }

                discovered.RejectionReasonCode = failure.ReasonCode;
                discovered.LastError = failure.Explanation;
                discovered.ProcessedAtUtc = DateTimeOffset.UtcNow;
                if (failure.HostShouldBeAvoided && !string.IsNullOrWhiteSpace(discovered.NormalizedDomain))
                {
                    discoveryContext.DeadDomains.Add(discovered.NormalizedDomain);
                }

                await _sourceRegistry.RecordFunnelEventAsync(
                    discovered.DiscoverySourceId,
                    accessible: false,
                    parsed: false,
                    relevantCompany: false,
                    liveVerified: false,
                    publicContact: false,
                    duplicate: false,
                    processingMs: fetch.DurationMs ?? 0,
                    cancellationToken);
                await PersistFailedPageAsync(run, discovered, fetch, cancellationToken);
                await RecordEventAsync(
                    run.Id,
                    ResolveFetchFailureEventType(failure),
                    failure.Kind == FetchFailureKind.RobotsDisallowed
                        ? ResearchRunEventSeverity.Warning
                        : ResearchRunEventSeverity.Error,
                    failure.Kind is FetchFailureKind.RobotsDisallowed or FetchFailureKind.SsrfBlocked
                        ? ResearchPipelineStage.Safety
                        : ResearchPipelineStage.Fetch,
                    failure.Explanation,
                    url: discovered.Url,
                    domain: discovered.NormalizedDomain,
                    durationMs: fetch.DurationMs,
                    httpStatusCode: fetch.StatusCode,
                    provider: Name,
                    data: new
                    {
                        reasonCode = failure.ReasonCode,
                        kind = failure.Kind.ToString(),
                        retryAppropriate = failure.RetryAppropriate,
                        hostAvoided = failure.HostShouldBeAvoided
                    },
                    cancellationToken: cancellationToken);
                continue;
            }

            var contentAdmission = UrlAdmissionPolicy.Evaluate(
                discovered.Url,
                fetch.ContentType,
                countries: discoveryContext.Countries);
            if (!contentAdmission.Accepted)
            {
                discovered.Status = DiscoveredUrlStatus.SkippedFiltered;
                discovered.RejectionReasonCode = contentAdmission.ReasonCode;
                discovered.PagePurpose = contentAdmission.PagePurpose.ToString();
                discovered.LastError = contentAdmission.Explanation;
                discovered.ProcessedAtUtc = DateTimeOffset.UtcNow;
                context.DiscoveryState.PagesSkipped++;
                context.DiscoveryState.FetchesSucceeded++;
                context.DiscoveryState.RecordUrlRejection(contentAdmission.ReasonCode);
                fetchesThisBatch++;
                context.DiscoveryState.PagesFetched++;
                context.DiscoveryState.FetchBudgetConsumed = context.DiscoveryState.PagesFetched;
                context.DiscoveryState.BudgetRemaining = Math.Max(
                    0,
                    budget.MaxFetchesPerRun - context.DiscoveryState.FetchBudgetConsumed);
                await PersistFailedPageAsync(run, discovered, fetch, cancellationToken);
                await RecordEventAsync(
                    run.Id,
                    ResearchRunEventType.UrlRejected,
                    ResearchRunEventSeverity.Info,
                    ResearchPipelineStage.Fetch,
                    contentAdmission.Explanation,
                    url: discovered.Url,
                    domain: discovered.NormalizedDomain,
                    durationMs: fetch.DurationMs,
                    httpStatusCode: fetch.StatusCode,
                    provider: Name,
                    data: new { reasonCode = contentAdmission.ReasonCode, contentType = fetch.ContentType },
                    cancellationToken: cancellationToken);
                continue;
            }

            var pageUri = fetch.FinalUrl ?? uri;
            var extractStartedAt = DateTimeOffset.UtcNow;
            var extracted = HtmlPageExtractor.Extract(fetch.Body, pageUri);
            context.DiscoveryState.RecordStageDuration(
                "parseExtract",
                (long)(DateTimeOffset.UtcNow - extractStartedAt).TotalMilliseconds);
            var page = await PersistCrawledPageAsync(run, discovered, fetch, extracted, cancellationToken);
            pagesPerDomain[domain] = pagesPerDomain.TryGetValue(domain, out var existing) ? existing + 1 : 1;
            run.PagesCrawled++;
            fetchesThisBatch++;
            context.DiscoveryState.PagesFetched++;
            context.DiscoveryState.FetchesSucceeded++;
            context.DiscoveryState.FetchBudgetConsumed = context.DiscoveryState.PagesFetched;
            context.DiscoveryState.BudgetRemaining = Math.Max(
                0,
                budget.MaxFetchesPerRun - context.DiscoveryState.FetchBudgetConsumed);
            discovered.Status = DiscoveredUrlStatus.Crawled;
            discovered.LiveVerificationStatus = LiveVerificationStatus.Parsed;
            discovered.LiveVerifiedAtUtc = DateTimeOffset.UtcNow;
            discovered.ProcessedAtUtc = DateTimeOffset.UtcNow;
            context.DiscoveryState.UrlsAccessible++;
            context.DiscoveryState.UrlsParsed++;

            var pagePurpose = UrlAdmissionPolicy.ClassifyPagePurpose(discovered.Url);
            var isDirectorySeed = ShouldSkipAsProspectSeed(discovered);
            context.DiscoveryState.CurrentInvestigationActivity = ResolveInvestigationActivity(
                discovered,
                pagePurpose,
                isDirectorySeed);
            var investigationStartedAt = DateTimeOffset.UtcNow;
            var investigation = WebsiteInvestigationEngine.InvestigateFetchedPage(
                new FetchedPageInvestigationRequest
                {
                    PageUrl = page.FinalUrl ?? discovered.Url,
                    NormalizedDomain = discovered.NormalizedDomain,
                    Html = fetch.Body,
                    Extracted = extracted,
                    UrlPurpose = pagePurpose,
                    SourceType = discovered.SourceType,
                    IsDirectorySeed = isDirectorySeed,
                    Depth = discovered.Depth,
                    MaxDepth = maxDepth,
                    Industries = discoveryContext.Industries,
                    Countries = discoveryContext.Countries,
                    DiscoveryState = context.DiscoveryState
                });
            context.DiscoveryState.RecordStageDuration(
                "investigation",
                (long)(DateTimeOffset.UtcNow - investigationStartedAt).TotalMilliseconds);
            await RecordInvestigationEventsAsync(run, discovered, investigation, cancellationToken);
            await PersistInvestigationEvidenceAsync(run, page, investigation, cancellationToken);
            await QueueInvestigationFollowUpsAsync(
                run,
                discovered,
                investigation,
                discoveryContext.Industries,
                discoveryContext.Countries,
                maxDepth,
                context.DiscoveryState,
                cancellationToken);

            var isDirectoryInvestigation = pagePurpose == UrlPagePurpose.DirectoryListing
                                           || isDirectorySeed
                                           || investigation.SiteRole == InvestigationSiteRole.Directory;
            var spaShell = PageContentAnalyzer.LooksLikeClientRenderedShell(fetch.Body, extracted);
            if (spaShell)
            {
                await RecordEventAsync(
                    run.Id,
                    ResearchRunEventType.SpaShellDetected,
                    ResearchRunEventSeverity.Warning,
                    ResearchPipelineStage.Parse,
                    $"Client-rendered shell detected on {discovered.Url}; listing HTML was empty and JS was not executed.",
                    url: discovered.Url,
                    domain: discovered.NormalizedDomain,
                    provider: Name,
                    data: new
                    {
                        reasonCode = "investigation.spa_shell_no_listings",
                        sameOriginLinks = extracted.SameOriginLinks.Count,
                        externalLinks = extracted.ExternalLinks.Count,
                        listings = investigation.Listings.Count
                    },
                    cancellationToken: cancellationToken);
            }

            if (enableSitemap
                && discovered.Depth == 0
                && UrlAdmissionPolicy.IsExpansionRoot(pagePurpose)
                && !UrlAdmissionPolicy.IsLowValueExpansionPurpose(pagePurpose)
                && (isDirectoryInvestigation
                    || (pagePurpose != UrlPagePurpose.DirectoryListing
                        && investigation.SiteRole != InvestigationSiteRole.Directory)))
            {
                await ExpandSitemapsAsync(
                    run,
                    discovered,
                    pageUri,
                    userAgent,
                    maxDepth,
                    discoveryContext.Industries,
                    discoveryContext.Countries,
                    cancellationToken);
            }

            if (enableLinkExpansion
                && discovered.Depth < maxDepth
                && !UrlAdmissionPolicy.IsLowValueExpansionPurpose(pagePurpose))
            {
                var linksForExpansion = extracted.SameOriginLinks.ToList();
                var externalForExpansion = extracted.ExternalLinks.ToList();
                if (isDirectoryInvestigation && (spaShell || linksForExpansion.Count + externalForExpansion.Count < 4))
                {
                    var embedded = PageContentAnalyzer.HarvestEmbeddedAbsoluteUrls(fetch.Body, pageUri);
                    foreach (var embeddedUrl in embedded)
                    {
                        if (UrlCanonicalizer.AreSameRegistrableDomain(discovered.NormalizedDomain, embeddedUrl)
                            || string.Equals(
                                UrlCanonicalizer.ExtractDomain(embeddedUrl),
                                discovered.NormalizedDomain,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            if (!linksForExpansion.Contains(embeddedUrl, StringComparer.OrdinalIgnoreCase))
                            {
                                linksForExpansion.Add(embeddedUrl);
                            }
                        }
                        else if (!externalForExpansion.Contains(embeddedUrl, StringComparer.OrdinalIgnoreCase))
                        {
                            externalForExpansion.Add(embeddedUrl);
                        }
                    }
                }

                if (discoveryContext.EnableContactPageHints || context.DiscoveryState.EnableContactPageHints)
                {
                    linksForExpansion = LiveVerificationHelper.PrioritizeContactPages(linksForExpansion);
                }

                context.DiscoveryState.LinksExtracted += linksForExpansion.Count + externalForExpansion.Count;
                await ExpandLinksAsync(
                    run,
                    discovered,
                    linksForExpansion,
                    externalForExpansion,
                    discoveryContext.Industries,
                    discoveryContext.Countries,
                    budget,
                    maxDepth,
                    isDirectoryInvestigation,
                    investigation.OrganizationCandidates.Count > 0 || investigation.Listings.Count > 0 || isDirectoryInvestigation,
                    context.DiscoveryState,
                    cancellationToken);
            }

            foreach (var listingCandidate in investigation.OrganizationCandidates)
            {
                context.DiscoveryState.OrganizationMentionsExtracted++;
                if (ProspectJunkClassifier.IsJunkOrganization(
                        listingCandidate.CompanyName,
                        listingCandidate.SourceUrl ?? listingCandidate.Website,
                        listingCandidate.CompanyDescription))
                {
                    context.DiscoveryState.IrrelevantEntitiesRejected++;
                    context.DiscoveryState.RecordRejection("Non-target entity type");
                    continue;
                }

                if (!CountryScopeHelper.HostMatchesCountries(
                        UrlCanonicalizer.ExtractDomain(listingCandidate.Website),
                        context.Countries))
                {
                    continue;
                }

                candidates.Add(listingCandidate);
                context.DiscoveryState.CandidatesDiscovered++;
                if (listingCandidate.NameConfidence >= 55
                    && !CompanyIdentityResolver.IsGenericName(listingCandidate.CompanyName))
                {
                    context.DiscoveryState.CompaniesIdentified++;
                    context.DiscoveryState.CandidateEntitiesExtracted++;
                }

                if (!string.IsNullOrWhiteSpace(listingCandidate.Email))
                {
                    context.DiscoveryState.PublicContactsExtracted++;
                    context.DiscoveryState.AttributedEmailsAccepted++;
                }

                RememberHostOrganization(
                    context.DiscoveryState,
                    UrlCanonicalizer.ExtractDomain(listingCandidate.Website) ?? domain,
                    listingCandidate);
            }

            var prospectCandidate = isDirectorySeed
                ? null
                : BuildCandidate(context, discovered, extracted, page);
            if (prospectCandidate is null && !isDirectorySeed && investigation.OrganizationCandidates.Count == 0)
            {
                context.DiscoveryState.PagesSkipped++;
                var rejectDiagnostics = ExplainBuildCandidateRejection(context, discovered, extracted, page);
                await RecordEventAsync(
                    run.Id,
                    ResearchRunEventType.Warning,
                    ResearchRunEventSeverity.Info,
                    ResearchPipelineStage.CompanyExtraction,
                    "Page skipped as prospect due to identity/entity filters.",
                    url: discovered.Url,
                    domain: discovered.NormalizedDomain,
                    provider: Name,
                    data: rejectDiagnostics,
                    cancellationToken: cancellationToken);
            }

            var identitySupported = prospectCandidate is not null
                && prospectCandidate.NameConfidence >= 55
                && LiveVerificationHelper.SupportsCompanyIdentity(
                    prospectCandidate.CompanyName,
                    extracted.Title,
                    extracted.Excerpt);
            var hasContact = !string.IsNullOrWhiteSpace(prospectCandidate?.Email)
                             || !string.IsNullOrWhiteSpace(prospectCandidate?.Phone);
            if (prospectCandidate is not null
                && !ProspectJunkClassifier.IsJunkOrganization(
                    prospectCandidate.CompanyName,
                    prospectCandidate.SourceUrl ?? prospectCandidate.Website,
                    prospectCandidate.CompanyDescription))
            {
                candidates.Add(prospectCandidate);
                context.DiscoveryState.CandidatesDiscovered++;
                context.DiscoveryState.CompaniesIdentified++;
                context.DiscoveryState.CandidateEntitiesExtracted++;
                context.DiscoveryState.PagesClassifiedRelevant++;
                RememberHostOrganization(context.DiscoveryState, domain, prospectCandidate);
                if (identitySupported)
                {
                    discovered.LiveVerificationStatus = LiveVerificationStatus.IdentitySupported;
                    context.DiscoveryState.LiveVerifiedCompanies++;
                }
                else
                {
                    discovered.LiveVerificationStatus = LiveVerificationStatus.Parsed;
                    context.DiscoveryState.BusinessesRequiringReview++;
                }

                if (hasContact)
                {
                    context.DiscoveryState.PublicContactsExtracted++;
                }
            }

            await _sourceRegistry.RecordFunnelEventAsync(
                discovered.DiscoverySourceId,
                accessible: true,
                parsed: true,
                relevantCompany: prospectCandidate is not null,
                liveVerified: identitySupported,
                publicContact: hasContact,
                duplicate: false,
                processingMs: 0,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            if (context.ReportProgressAsync is not null)
            {
                await context.ReportProgressAsync(context.DiscoveryState, cancellationToken);
            }
        }

        run.UrlsDiscovered = await _db.DiscoveredUrls.CountAsync(x => x.ResearchRunId == run.Id, cancellationToken);
        var pendingRemaining = await _db.DiscoveredUrls.CountAsync(
            x => x.ResearchRunId == run.Id && x.Status == DiscoveredUrlStatus.Pending,
            cancellationToken);
        context.DiscoveryState.PagesQueued = pendingRemaining;
        await _db.SaveChangesAsync(cancellationToken);

        context.DiscoveryState.UsefulPendingUrls = pendingRemaining == 0
            ? 0
            : CrawlQueueScheduler.CountUsefulPending(
                await _db.DiscoveredUrls
                    .Where(x => x.ResearchRunId == run.Id && x.Status == DiscoveredUrlStatus.Pending)
                    .ToListAsync(cancellationToken));

        context.DiscoveryState.BudgetRemaining = Math.Max(
            0,
            budget.MaxFetchesPerRun - context.DiscoveryState.FetchBudgetConsumed);
        context.DiscoveryState.MoreEligibleWorkRemained =
            pendingRemaining > 0
            && context.DiscoveryState.PagesFetched < budget.MaxFetchesPerRun
            && !context.DiscoveryState.LimitsExhausted;

        var searchMayStillSupplyUrls = pendingRemaining == 0
            && !context.DiscoveryState.CompletedSourceNames.Contains("WebSearch")
            && !context.DiscoveryState.SearchLimitsExhausted;
        if ((pendingRemaining == 0 && !searchMayStillSupplyUrls)
            || context.DiscoveryState.PagesFetched >= budget.MaxFetchesPerRun
            || context.DiscoveryState.LimitsExhausted)
        {
            context.DiscoveryState.CompletedSourceNames.Add(Name);
            if (context.DiscoveryState.PagesFetched >= budget.MaxFetchesPerRun)
            {
                context.DiscoveryState.LimitsExhausted = true;
                context.DiscoveryState.LimitsExhaustedReason ??=
                    $"Maximum page fetch budget reached with {context.DiscoveryState.UsefulPendingUrls} useful URL(s) still pending.";
                context.DiscoveryState.LastBudgetStopReason = "fetch.run_budget_exhausted";
                context.DiscoveryState.DiscoveryStopReason = DiscoveryStopReason.BudgetExhausted;
                context.DiscoveryState.UsefulUrlsLeftPendingAtStop = context.DiscoveryState.UsefulPendingUrls;
                context.DiscoveryState.MoreEligibleWorkRemained = context.DiscoveryState.UsefulPendingUrls > 0;
            }
            else if (pendingRemaining == 0 && string.IsNullOrWhiteSpace(context.DiscoveryState.LastBudgetStopReason))
            {
                context.DiscoveryState.LastBudgetStopReason = "fetch.queue_empty";
                context.DiscoveryState.MoreEligibleWorkRemained = false;
            }

            context.DiscoveryState.CurrentInvestigationActivity = context.DiscoveryState.MoreEligibleWorkRemained
                ? "blocked_by_limitation"
                : "finished";

            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.StageCompleted,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.RunLifecycle,
                context.DiscoveryState.LastBudgetStopReason ?? "Independent crawl batch completed.",
                provider: Name,
                data: new
                {
                    reasonCode = context.DiscoveryState.LastBudgetStopReason,
                    pagesFetched = context.DiscoveryState.PagesFetched,
                    fetchBudget = budget.MaxFetchesPerRun,
                    pagesQueued = pendingRemaining,
                    usefulPending = context.DiscoveryState.UsefulPendingUrls
                },
                cancellationToken: cancellationToken);
        }

        return candidates;
    }

    private async Task MaybeRunYieldRecoveryAsync(
        ProspectSourceContext context,
        UrlDiscoveryContext discoveryContext,
        ResearchRun run,
        DiscoveryBudget budget,
        CancellationToken cancellationToken)
    {
        if (context.DiscoveryState.YieldRecoveryAttempted
            || context.DiscoveryState.PagesFetched < 8
            || context.DiscoveryState.PagesFetched >= budget.MaxFetchesPerRun)
        {
            return;
        }

        var yield = new DiscoveryYieldSignals
        {
            RequestedProspects = Math.Max(run.RequestedProspectCount, 1),
            QualifiedProspects = run.QualifiedCount,
            UniqueTargetBusinesses = context.DiscoveryState.CompaniesIdentified,
            OfficialDomainVerified = context.DiscoveryState.LiveVerifiedCompanies,
            AttributedEmails = context.DiscoveryState.AttributedEmailsAccepted,
            HtmlFetchesSucceeded = context.DiscoveryState.UrlsAccessible,
            FetchesAttempted = context.DiscoveryState.PagesFetched,
            IrrelevantEntities = context.DiscoveryState.IrrelevantEntitiesRejected,
            EntitiesClassified = context.DiscoveryState.CompaniesIdentified
                                 + context.DiscoveryState.IrrelevantEntitiesRejected,
            Duplicates = run.DuplicatesFound,
            CandidatesProcessed = context.DiscoveryState.CandidatesValidated,
            FreshSourceUrls = context.DiscoveryState.SourcesAdded,
            PriorSourceUrls = context.DiscoveryState.SourcesReused,
            SourceFailures = context.DiscoveryState.SourcesUnavailable,
            SourcesAttempted = context.DiscoveryState.AutomaticDiscoveryProviderStats.Count(x => x.Attempted),
            AssetRejections = context.DiscoveryState.UrlsRejectedFiltered,
            UrlsDiscovered = run.UrlsDiscovered,
            UsefulPendingUrls = context.DiscoveryState.UsefulPendingUrls
        };

        if (!yield.IsUnderperforming())
        {
            return;
        }

        var interim = new AutomaticDiscoveryResult
        {
            Candidates = [],
            ProviderStats = context.DiscoveryState.AutomaticDiscoveryProviderStats.ToList(),
            DuplicatesRemoved = context.DiscoveryState.AutomaticDiscoveryDuplicatesRemoved,
            RejectedFiltered = context.DiscoveryState.UrlsRejectedFiltered
        };
        var action = AdaptiveDiscoveryRecovery.DecideFromOutcomes(
            discoveryContext,
            interim,
            yield,
            duplicateRatePercent: (int)Math.Round(yield.DuplicatePercent),
            failedFetchPercent: (int)Math.Round(100 - yield.HtmlFetchSuccessPercent));
        if (action is null)
        {
            return;
        }

        context.DiscoveryState.YieldRecoveryAttempted = true;
        context.DiscoveryState.RecoveryPasses++;
        context.DiscoveryState.LastRecoveryTrigger = action.TriggerSignal;
        context.DiscoveryState.LastRecoveryStrategy = action.Strategy;
        AdaptiveDiscoveryRecovery.Apply(discoveryContext, action);
        context.DiscoveryState.EnableContactPageHints = discoveryContext.EnableContactPageHints;
        context.DiscoveryState.RecoveryStrategies.Add(action.Strategy);

        var beforeUnique = context.DiscoveryState.UniqueDomainsDiscovered;
        var candidatesAdded = 0;
        var recoveryOutcome = "recovery.provider_pass";
        var recoveryBudgetSeconds = Math.Clamp(budget.RequestTimeoutSeconds * 2, 15, 45);
        using var recoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        recoveryCts.CancelAfter(TimeSpan.FromSeconds(recoveryBudgetSeconds));
        var recoveryToken = recoveryCts.Token;

        try
        {
            if (action.PreferOfficialCompanyDomains)
            {
                var official = await RecoverOfficialDomainsFromCrawledPagesAsync(
                    run,
                    discoveryContext,
                    budget,
                    recoveryToken);
                candidatesAdded += official.Candidates.Count;
                recoveryOutcome = official.OutcomeReason;
                if (official.Candidates.Count > 0)
                {
                    await PersistDiscoveredUrlsAsync(
                        run,
                        official.Candidates,
                        recoveryToken,
                        discoveryContext.Industries,
                        discoveryContext.Countries,
                        context.DiscoveryState);
                }
            }

            var recoveryResult = await _discoveryCoordinator.DiscoverAsync(discoveryContext, recoveryToken);
            if (recoveryResult.Candidates.Count > 0)
            {
                await PersistDiscoveredUrlsAsync(
                    run,
                    recoveryResult.Candidates,
                    recoveryToken,
                    discoveryContext.Industries,
                    discoveryContext.Countries,
                    context.DiscoveryState);
                candidatesAdded += recoveryResult.Candidates.Count;
            }

            var afterUnique = CountUniqueDomainsInQueue(run.Id);
            var improved = afterUnique > beforeUnique || candidatesAdded > 0;
            context.DiscoveryState.LastRecoveryImprovedYield = improved;
            context.DiscoveryState.RecoveryTelemetry.Add(new AdaptiveRecoveryTelemetry
            {
                StageNumber = discoveryContext.DiscoveryStage,
                TriggerSignal = action.TriggerSignal ?? "yield",
                Strategy = action.Strategy,
                Reason = action.Reason,
                CandidatesBefore = beforeUnique,
                CandidatesAfter = afterUnique,
                ImprovedYield = improved
            });

            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.RecoveryTriggered,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.Discovery,
                improved
                    ? $"{action.Strategy}: {action.Reason}"
                    : $"{action.Strategy}: zero-yield recovery completed.",
                provider: Name,
                data: new
                {
                    trigger = action.TriggerSignal,
                    strategy = action.Strategy,
                    improved,
                    candidatesAdded,
                    outcomeReason = improved ? recoveryOutcome : "recovery.zero_yield",
                    providerCandidates = recoveryResult.Candidates.Count,
                    qualifiedYieldPercent = yield.QualifiedYieldPercent,
                    budgetSeconds = recoveryBudgetSeconds
                },
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            context.DiscoveryState.LastRecoveryImprovedYield = false;
            context.DiscoveryState.RecoveryTelemetry.Add(new AdaptiveRecoveryTelemetry
            {
                StageNumber = discoveryContext.DiscoveryStage,
                TriggerSignal = action.TriggerSignal ?? "yield",
                Strategy = action.Strategy,
                Reason = "Recovery budget exhausted before completion.",
                CandidatesBefore = beforeUnique,
                CandidatesAfter = beforeUnique,
                ImprovedYield = false
            });
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.Warning,
                ResearchRunEventSeverity.Warning,
                ResearchPipelineStage.Discovery,
                "Yield recovery stopped after bounded execution budget.",
                provider: Name,
                data: new
                {
                    reasonCode = "recovery.budget_exhausted",
                    strategy = action.Strategy,
                    budgetSeconds = recoveryBudgetSeconds,
                    candidatesAdded
                },
                cancellationToken: cancellationToken);
        }
    }

    private async Task RejectUnsafePendingUrlsAsync(
        ProspectSourceContext context,
        ResearchRun run,
        List<DiscoveredUrl> pending,
        CancellationToken cancellationToken)
    {
        var rejected = 0;
        foreach (var item in pending)
        {
            var admission = UrlAdmissionPolicy.Evaluate(item.Url, countries: context.Countries);
            if (admission.Accepted)
            {
                continue;
            }

            item.Status = DiscoveredUrlStatus.SkippedFiltered;
            item.RejectionReasonCode = admission.ReasonCode;
            item.PagePurpose = admission.PagePurpose.ToString();
            item.LastError = admission.Explanation;
            item.ProcessedAtUtc = DateTimeOffset.UtcNow;
            context.DiscoveryState.PagesSkipped++;
            context.DiscoveryState.FetchesSkipped++;
            context.DiscoveryState.RecordUrlRejection(admission.ReasonCode);
            rejected++;
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.UrlRejected,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.Safety,
                admission.Explanation,
                url: item.Url,
                domain: item.NormalizedDomain,
                provider: Name,
                data: new
                {
                    reasonCode = admission.ReasonCode,
                    source = "pending_revalidation"
                },
                cancellationToken: cancellationToken);
        }

        if (rejected > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<OfficialDomainRecoveryResult> RecoverOfficialDomainsFromCrawledPagesAsync(
        ResearchRun run,
        UrlDiscoveryContext discoveryContext,
        DiscoveryBudget budget,
        CancellationToken cancellationToken)
    {
        var pagesQuery = _db.CrawledPages.AsNoTracking()
            .Where(x => x.ResearchRunId == run.Id
                        && x.HttpStatusCode >= 200
                        && x.HttpStatusCode < 400
                        && x.ExternalLinksJson != null);
        var runId = run.Id.ToString("D").ToUpperInvariant();
        var pages = await SqliteDateTimeOffsetQuery.TakeRecentByStoredUtcAsync(
            _db,
            pagesQuery,
            x => x.CrawledAtUtc,
            $"""
             SELECT * FROM CrawledPages
             WHERE ResearchRunId = {runId}
               AND HttpStatusCode >= 200
               AND HttpStatusCode < 400
               AND ExternalLinksJson IS NOT NULL
             ORDER BY CrawledAtUtc DESC
             LIMIT {40}
             """,
            40,
            cancellationToken);
        var knownDomains = await _db.DiscoveredUrls.AsNoTracking()
            .Where(x => x.ResearchRunId == run.Id && x.NormalizedDomain != null)
            .Select(x => x.NormalizedDomain!)
            .Distinct()
            .ToListAsync(cancellationToken);
        var knownUrls = await _db.DiscoveredUrls.AsNoTracking()
            .Where(x => x.ResearchRunId == run.Id && x.NormalizedUrl != null)
            .Select(x => x.NormalizedUrl!)
            .ToListAsync(cancellationToken);

        var links = new List<string>();
        foreach (var page in pages)
        {
            var purpose = Enum.TryParse<UrlPagePurpose>(page.PagePurpose, true, out var parsed)
                ? parsed
                : UrlAdmissionPolicy.ClassifyPagePurpose(page.Url);
            if (purpose is not UrlPagePurpose.DirectoryListing
                && !SystemDirectoryCatalog.IsSeedOrDirectoryHost(page.NormalizedDomain ?? page.Url))
            {
                continue;
            }

            links.AddRange(DeserializeExternalLinks(page.ExternalLinksJson));
        }

        return OfficialDomainRecovery.BuildCandidates(
            links,
            parentUrl: null,
            knownDomains,
            knownUrls,
            discoveryContext.Industries,
            discoveryContext.Countries,
            Math.Min(budget.MaxExternalExpansionsPerPage * 4, 24));
    }

    private int CountUniqueDomainsInQueue(Guid researchRunId)
    {
        return _db.DiscoveredUrls.AsNoTracking()
            .Where(x => x.ResearchRunId == researchRunId && x.NormalizedDomain != null)
            .Select(x => x.NormalizedDomain!)
            .Distinct()
            .Count();
    }

    private static string? SerializeExternalLinks(IReadOnlyList<string> links)
    {
        if (links.Count == 0)
        {
            return null;
        }

        var trimmed = links.Take(40).ToList();
        return System.Text.Json.JsonSerializer.Serialize(trimmed);
    }

    private static List<string> DeserializeExternalLinks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static bool IsDurationExceeded(ResearchDiscoveryState state, int maxMinutes)
    {
        var minutes = Math.Clamp(maxMinutes, 1, 240);
        return DateTimeOffset.UtcNow - state.StartedAtUtc > TimeSpan.FromMinutes(minutes);
    }

    private UrlDiscoveryContext BuildDiscoveryContext(
        ProspectSourceContext context,
        ResearchRun run,
        ResearchQueryVersion? version)
    {
        var budget = DiscoveryBudgetResolver.Resolve(_options, version);
        var industries = MergeDistinct(context.Industries, version?.TargetIndustry);
        var synonyms = IndustrySynonymCatalog.Expand(industries);
        var availableProviders = _discoverySources.Select(x => x.Name).Distinct().ToList();
        var plan = DiscoveryQueryPlanner.Build(version, context, budget, availableProviders);

        return new UrlDiscoveryContext
        {
            ProjectId = context.ProjectId,
            ResearchRunId = run.Id,
            ResearchQueryId = run.ResearchQueryId,
            ResearchQueryVersionId = run.ResearchQueryVersionId,
            MaxUrls = budget.MaxDiscoveryCandidates,
            MaxCandidateDomains = budget.MaxCandidateDomains,
            MaxPriorResearchSharePercent = budget.MaxPriorResearchSharePercent,
            MaxArchiveRecords = budget.MaxArchiveRecords,
            MaxDiscoveryStages = budget.MaxDiscoveryStages,
            MinCandidatesBeforeRecovery = plan.MinCandidatesBeforeRecovery,
            EnableDirectoryDiscovery = version?.EnableDirectoryDiscovery ?? true,
            EnableSitemapDiscovery = version?.EnableSitemapDiscovery ?? true,
            EnableLinkExpansion = version?.EnableLinkExpansion ?? true,
            EnablePriorResearchUrls = version?.EnablePriorResearchUrls ?? true,
            EnableWebArchiveDiscovery = _options.EnableCommonCrawlDiscovery,
            Countries = MergeDistinct(context.Countries, version?.TargetCountry),
            Industries = industries,
            IndustrySynonyms = synonyms,
            Plan = plan
        };
    }

    private static List<string> MergeDistinct(List<string> values, string? extra)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value.Trim()))
            {
                result.Add(value.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(extra) && seen.Add(extra.Trim()))
        {
            result.Add(extra.Trim());
        }

        return result;
    }

    private async Task PersistDiscoveredUrlsAsync(
        ResearchRun run,
        List<DiscoveredUrlCandidate> candidates,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? industries = null,
        IReadOnlyList<string>? countries = null,
        ResearchDiscoveryState? discoveryState = null)
    {
        var existing = await _db.DiscoveredUrls
            .Where(x => x.ResearchRunId == run.Id)
            .Select(x => x.NormalizedUrl)
            .ToListAsync(cancellationToken);
        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var repetitionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var industryList = industries ?? Array.Empty<string>();
        var countryList = countries ?? Array.Empty<string>();

        var admitted = 0;
        var rejected = 0;
        foreach (var candidate in candidates)
        {
            var admission = UrlAdmissionPolicy.Evaluate(candidate.Url, countries: countryList, industries: industryList);
            if (!admission.Accepted || string.IsNullOrWhiteSpace(admission.NormalizedUrl))
            {
                rejected++;
                discoveryState?.RecordUrlRejection(admission.ReasonCode);
                continue;
            }

            var normalized = admission.NormalizedUrl;
            var fingerprint = UrlAdmissionPolicy.BuildRepetitionFingerprint(normalized);
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                if (!repetitionCounts.TryGetValue(fingerprint, out var seenCount))
                {
                    seenCount = 0;
                }

                if (seenCount >= 5)
                {
                    rejected++;
                    discoveryState?.RecordUrlRejection("url.repetitive_detail");
                    continue;
                }

                repetitionCounts[fingerprint] = seenCount + 1;
            }

            if (!seen.Add(normalized))
            {
                continue;
            }

            var priority = UrlPriorityScorer.Score(
                normalized,
                candidate.SourceType,
                industryList,
                countryList,
                parentDomain: admission.NormalizedDomain,
                depth: candidate.Depth,
                alreadyCrawled: false,
                pagePurpose: admission.PagePurpose,
                isFreshSource: candidate.SourceType is not DiscoverySourceType.PriorResearch);
            var boostedTotal = Math.Max(0, priority.Total + Math.Max(0, candidate.PriorityBoost));

            _db.DiscoveredUrls.Add(new DiscoveredUrl
            {
                ResearchRunId = run.Id,
                ProjectId = run.ProjectId,
                Url = candidate.Url,
                NormalizedUrl = normalized,
                NormalizedDomain = admission.NormalizedDomain ?? UrlCanonicalizer.ExtractDomain(candidate.Url),
                SourceType = candidate.SourceType,
                SourceName = string.IsNullOrWhiteSpace(candidate.DiscoveryReasonCode)
                    ? candidate.SourceName
                    : candidate.DiscoveryReasonCode,
                ParentDiscoveredUrlId = candidate.ParentDiscoveredUrlId,
                DiscoverySourceId = candidate.DiscoverySourceId,
                Status = DiscoveredUrlStatus.Pending,
                LiveVerificationStatus = LiveVerificationStatus.NotAttempted,
                Depth = candidate.Depth,
                PagePurpose = admission.PagePurpose.ToString(),
                PriorityScore = boostedTotal,
                RejectionReasonCode = null,
                DiscoveredAtUtc = DateTimeOffset.UtcNow
            });
            admitted++;
            if (discoveryState is not null)
            {
                discoveryState.UrlsAdmitted++;
            }
        }

        run.UrlsDiscovered = seen.Count;
        await _db.SaveChangesAsync(cancellationToken);
        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.UrlDiscovered,
            ResearchRunEventSeverity.Info,
            ResearchPipelineStage.Discovery,
            $"Admitted {admitted} of {candidates.Count} candidate URLs ({rejected} filtered).",
            data: new { count = candidates.Count, admitted, rejected, unique = seen.Count },
            provider: Name,
            cancellationToken: cancellationToken);
    }

    private async Task ExpandSitemapsAsync(
        ResearchRun run,
        DiscoveredUrl parent,
        Uri siteUri,
        string userAgent,
        int maxDepth,
        List<string> industries,
        List<string> countries,
        CancellationToken cancellationToken)
    {
        try
        {
            var sitemapUrls = await _robots.GetSitemapUrlsAsync(siteUri, userAgent, cancellationToken);
            if (sitemapUrls.Count == 0)
            {
                sitemapUrls = [new Uri(siteUri, "/sitemap.xml")];
            }

            foreach (var sitemapUri in sitemapUrls.Take(3))
            {
                var fetch = await _fetcher.FetchAsync(
                    new SafeHttpFetchRequest { Url = sitemapUri },
                    cancellationToken);
                if (!fetch.Succeeded || string.IsNullOrWhiteSpace(fetch.Body))
                {
                    continue;
                }

                var urls = _sitemapParser.Parse(fetch.Body, sitemapUri);
                var expansions = urls
                    .Select(u =>
                    {
                        var admission = UrlAdmissionPolicy.Evaluate(u.ToString(), countries: countries, industries: industries);
                        return new
                        {
                            Url = u.ToString(),
                            Admission = admission,
                            Score = admission.Accepted
                                ? UrlPriorityScorer.Score(
                                    u.ToString(),
                                    DiscoverySourceType.Sitemap,
                                    industries,
                                    countries,
                                    parent.NormalizedDomain,
                                    parent.Depth + 1,
                                    alreadyCrawled: false,
                                    pagePurpose: admission.PagePurpose)
                                : new UrlPriorityScore()
                        };
                    })
                    .Where(x => x.Admission.Accepted
                                && !UrlAdmissionPolicy.IsLowValueExpansionPurpose(x.Admission.PagePurpose)
                                && (UrlAdmissionPolicy.IsUsefulPendingPurpose(x.Admission.PagePurpose)
                                    || x.Score.IsDiscoveryUseful
                                    || x.Score.IsVerificationUseful))
                    .OrderByDescending(x => x.Score.Total)
                    .ThenBy(x => x.Url, StringComparer.Ordinal)
                    .Take(25)
                    .Select(x => new DiscoveredUrlCandidate
                    {
                        Url = x.Url,
                        SourceType = DiscoverySourceType.Sitemap,
                        SourceName = "Sitemap",
                        ParentDiscoveredUrlId = parent.Id,
                        Depth = parent.Depth + 1
                    })
                    .ToList();

                if (expansions.Count > 0 && parent.Depth < maxDepth)
                {
                    await PersistDiscoveredUrlsAsync(
                        run,
                        expansions,
                        cancellationToken,
                        industries,
                        countries);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sitemap expansion failed for {Url}", siteUri);
        }
    }

    private async Task ExpandLinksAsync(
        ResearchRun run,
        DiscoveredUrl parent,
        List<string> sameOriginLinks,
        List<string> externalLinks,
        List<string> industries,
        List<string> countries,
        DiscoveryBudget budget,
        int maxDepth,
        bool preferOfficialExternalDomains,
        bool parentYieldedRelevantEvidence,
        ResearchDiscoveryState discoveryState,
        CancellationToken cancellationToken)
    {
        if (parent.Depth >= maxDepth)
        {
            return;
        }

        var parentDomain = parent.NormalizedDomain ?? UrlCanonicalizer.ExtractDomain(parent.Url);
        var clusterLinks = new List<string>(sameOriginLinks);
        var trueExternal = new List<string>();
        var siblingDirectoryCount = 0;
        foreach (var link in externalLinks)
        {
            var candidateDomain = UrlCanonicalizer.ExtractDomain(link);
            if (preferOfficialExternalDomains
                && IsRelatedDirectoryClusterHost(parentDomain, candidateDomain))
            {
                clusterLinks.Add(link);
                siblingDirectoryCount++;
                continue;
            }

            trueExternal.Add(link);
        }

        if (siblingDirectoryCount > 0)
        {
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.DirectorySiblingQueued,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.Discovery,
                $"Queued {siblingDirectoryCount} related directory host link(s) from {parent.Url}.",
                url: parent.Url,
                domain: parentDomain,
                provider: Name,
                data: new
                {
                    reasonCode = "investigation.directory_sibling_host",
                    count = siblingDirectoryCount
                },
                cancellationToken: cancellationToken);
        }

        var scoredInternal = clusterLinks
            .Select(link =>
            {
                var admission = UrlAdmissionPolicy.Evaluate(link, countries: countries, industries: industries);
                var linkKind = PageContentAnalyzer.ClassifyLink(link, preferOfficialExternalDomains);
                return new
                {
                    Url = link,
                    Admission = admission,
                    LinkKind = linkKind,
                    Score = admission.Accepted
                        ? UrlPriorityScorer.Score(
                            link,
                            DiscoverySourceType.LinkExpansion,
                            industries,
                            countries,
                            parentDomain,
                            parent.Depth + 1,
                            alreadyCrawled: false,
                            pagePurpose: admission.PagePurpose)
                        : new UrlPriorityScore()
                };
            })
            .Where(x =>
            {
                if (!x.Admission.Accepted
                    || UrlAdmissionPolicy.IsLowValueExpansionPurpose(x.Admission.PagePurpose))
                {
                    return false;
                }

                if (preferOfficialExternalDomains)
                {
                    if (IsDirectoryChromePath(x.Url))
                    {
                        return false;
                    }

                    return PageContentAnalyzer.IsInternalInvestigationTarget(x.LinkKind)
                           || x.Admission.PagePurpose is UrlPagePurpose.DirectoryListing
                               or UrlPagePurpose.CompanyProfile
                               or UrlPagePurpose.Contact
                               or UrlPagePurpose.About
                               or UrlPagePurpose.Team
                               or UrlPagePurpose.Location
                           || LooksLikeDirectoryChildPath(x.Url)
                           || IsRelatedDirectoryClusterHost(parentDomain, x.Admission.NormalizedDomain);
                }

                return x.Score.IsVerificationUseful
                       || x.Score.IsDiscoveryUseful
                       || UrlAdmissionPolicy.IsUsefulPendingPurpose(x.Admission.PagePurpose);
            })
            .OrderByDescending(x => x.Score.Total)
            .Take(CrawlQueueScheduler.ResolveLinkExpansionCap(
                budget.MaxLinksPerPage,
                parentYieldedRelevantEvidence || preferOfficialExternalDomains))
            .ToList();

        var externalThreshold = preferOfficialExternalDomains ? 24 : 40;
        var scoredExternal = trueExternal
            .Select(link =>
            {
                var admission = UrlAdmissionPolicy.Evaluate(link, countries: countries, industries: industries);
                return new
                {
                    Url = link,
                    Admission = admission,
                    Score = admission.Accepted
                        ? UrlPriorityScorer.Score(
                            link,
                            DiscoverySourceType.LinkExpansion,
                            industries,
                            countries,
                            parentDomain,
                            parent.Depth + 1,
                            alreadyCrawled: false,
                            pagePurpose: admission.PagePurpose)
                        : new UrlPriorityScore()
                };
            })
            .Where(x => x.Admission.Accepted
                        && !UrlAdmissionPolicy.IsLowValueExpansionPurpose(x.Admission.PagePurpose)
                        && x.Admission.PagePurpose != UrlPagePurpose.DirectoryListing
                        && !SystemDirectoryCatalog.IsSeedOrDirectoryHost(x.Admission.NormalizedDomain)
                        && (x.Score.IsDiscoveryUseful || x.Score.Total >= externalThreshold))
            .OrderByDescending(x => x.Score.Total)
            .Take(parentYieldedRelevantEvidence || preferOfficialExternalDomains
                ? Math.Clamp(budget.MaxExternalExpansionsPerPage, 0, 25)
                : 0)
            .ToList();

        var expansions = scoredInternal
            .Select(x => new DiscoveredUrlCandidate
            {
                Url = x.Url,
                SourceType = DiscoverySourceType.LinkExpansion,
                SourceName = IsRelatedDirectoryClusterHost(parentDomain, x.Admission.NormalizedDomain)
                    && !string.Equals(parentDomain, x.Admission.NormalizedDomain, StringComparison.OrdinalIgnoreCase)
                    ? "Directory sibling link"
                    : "Internal link",
                DiscoveryReasonCode = IsRelatedDirectoryClusterHost(parentDomain, x.Admission.NormalizedDomain)
                    && !string.Equals(parentDomain, x.Admission.NormalizedDomain, StringComparison.OrdinalIgnoreCase)
                    ? "investigation.directory_sibling_host"
                    : "priority.internal_expansion",
                ParentDiscoveredUrlId = parent.Id,
                Depth = parent.Depth + 1,
                PriorityBoost = preferOfficialExternalDomains ? 12 : 0
            })
            .Concat(scoredExternal.Select(x => new DiscoveredUrlCandidate
            {
                Url = x.Url,
                SourceType = DiscoverySourceType.LinkExpansion,
                SourceName = "External link",
                DiscoveryReasonCode = "priority.external_expansion",
                ParentDiscoveredUrlId = parent.Id,
                Depth = parent.Depth + 1
            }))
            .ToList();

        expansions = CrawlQueueScheduler.ApplyPerDomainCap(
                expansions,
                x => UrlCanonicalizer.ExtractDomain(x.Url),
                budget.MaxLinksPerDomain)
            .Take(Math.Max(1, budget.MaxLinksPerPage + Math.Clamp(budget.MaxExternalExpansionsPerPage, 0, 25)))
            .ToList();
        discoveryState.LinksAdmitted += expansions.Count;
        discoveryState.LinksRejected += Math.Max(
            0,
            sameOriginLinks.Count + externalLinks.Count - expansions.Count);

        if (expansions.Count > 0)
        {
            await PersistDiscoveredUrlsAsync(
                run,
                expansions,
                cancellationToken,
                industries,
                countries);
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.UrlDiscovered,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.Discovery,
                $"Expanded {expansions.Count} prioritized links from {parent.Url}",
                url: parent.Url,
                domain: parentDomain,
                provider: Name,
                data: new
                {
                    reasonCode = "priority.expansion_accepted",
                    internalCount = scoredInternal.Count,
                    externalCount = scoredExternal.Count,
                    directorySiblingCount = siblingDirectoryCount
                },
                cancellationToken: cancellationToken);
        }
    }

    private static bool IsRelatedDirectoryClusterHost(string? parentDomain, string? candidateDomain)
    {
        if (string.IsNullOrWhiteSpace(parentDomain) || string.IsNullOrWhiteSpace(candidateDomain))
        {
            return false;
        }

        if (string.Equals(parentDomain, candidateDomain, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!UrlCanonicalizer.AreSameRegistrableDomain(parentDomain, candidateDomain))
        {
            return false;
        }

        return SystemDirectoryCatalog.IsSeedOrDirectoryHost(parentDomain)
               || SystemDirectoryCatalog.IsSeedOrDirectoryHost(candidateDomain);
    }

    private static bool IsDirectoryChromePath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.Contains("/auth/", StringComparison.Ordinal)
               || path.Contains("/login", StringComparison.Ordinal)
               || path.Contains("/assets/", StringComparison.Ordinal)
               || path.EndsWith(".pdf", StringComparison.Ordinal)
               || path.Contains("create-account", StringComparison.Ordinal)
               || path.Contains("redirect-register", StringComparison.Ordinal)
               || path.Contains("otp", StringComparison.Ordinal)
               || path.Contains("qr-secreen", StringComparison.Ordinal)
               || path.Contains("service_level", StringComparison.Ordinal)
               || path.Contains("communication_channel", StringComparison.Ordinal)
               || path.EndsWith("/contact", StringComparison.Ordinal)
                  && path.Contains("school-search", StringComparison.Ordinal);
    }

    private async Task<CrawledPage> PersistCrawledPageAsync(
        ResearchRun run,
        DiscoveredUrl discovered,
        SafeHttpFetchResult fetch,
        ExtractedPageData extracted,
        CancellationToken cancellationToken)
    {
        var page = new CrawledPage
        {
            ResearchRunId = run.Id,
            DiscoveredUrlId = discovered.Id,
            ProjectId = run.ProjectId,
            Url = discovered.Url,
            NormalizedUrl = discovered.NormalizedUrl,
            NormalizedDomain = discovered.NormalizedDomain,
            HttpStatusCode = fetch.StatusCode,
            ContentType = fetch.ContentType,
            ContentLengthBytes = fetch.ContentLengthBytes,
            ContentHash = extracted.ContentHash,
            CanonicalUrl = extracted.CanonicalUrl,
            FinalUrl = fetch.FinalUrl?.ToString(),
            RobotsAllowed = true,
            TextExcerpt = extracted.Excerpt,
            Title = extracted.Title,
            ExternalLinksJson = SerializeExternalLinks(extracted.ExternalLinks),
            PagePurpose = UrlAdmissionPolicy.ClassifyPagePurpose(discovered.Url).ToString(),
            CrawlDepth = discovered.Depth,
            DurationMs = fetch.DurationMs,
            RedirectCount = fetch.RedirectCount,
            WasRedirected = fetch.WasRedirected,
            BlockedBySsrf = fetch.BlockedBySsrf,
            CrawledAtUtc = DateTimeOffset.UtcNow
        };

        _db.CrawledPages.Add(page);
        await _db.SaveChangesAsync(cancellationToken);

        var isDirectorySource = discovered.SourceType is DiscoverySourceType.DirectoryCatalog
            or DiscoverySourceType.IndustryAssociation
            or DiscoverySourceType.WebArchive;
        foreach (var fact in extracted.Facts)
        {
            var notes = isDirectorySource
                ? "Extracted from third-party directory or archive page; not an official company website contact."
                : "Extracted from crawled page during live verification.";
            if (fact.FactType is "email" or "phone")
            {
                notes = isDirectorySource
                    ? "Public contact found on directory/archive listing; not verified as official company contact."
                    : "Public contact found on live page; deliverability not verified.";
            }

            if (!string.IsNullOrWhiteSpace(fact.ExtractionMethod))
            {
                notes = $"{notes} method={fact.ExtractionMethod}";
            }

            _db.ResearchEvidenceItems.Add(new ResearchEvidence
            {
                ProjectId = run.ProjectId,
                ResearchRunId = run.Id,
                CrawledPageId = page.Id,
                FactType = fact.FactType,
                FactValue = fact.FactValue,
                ConfidenceKind = fact.ConfidenceKind,
                SourceUrl = fact.SourceUrl ?? discovered.Url,
                Notes = notes,
                FirstDiscoveredAtUtc = DateTimeOffset.UtcNow,
                LastVerifiedAtUtc = DateTimeOffset.UtcNow
            });
        }

        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.PageCrawled,
            ResearchRunEventSeverity.Info,
            ResearchPipelineStage.Parse,
            $"Crawled {discovered.Url}",
            url: discovered.Url,
            domain: discovered.NormalizedDomain,
            durationMs: fetch.DurationMs,
            httpStatusCode: fetch.StatusCode,
            relatedEntityId: page.Id,
            relatedEntityKind: "crawledPage",
            data: new
            {
                title = extracted.Title,
                contentLengthBytes = fetch.ContentLengthBytes,
                redirectCount = fetch.RedirectCount,
                factCount = extracted.Facts.Count
            },
            provider: Name,
            cancellationToken: cancellationToken);

        var contactFacts = extracted.Facts.Where(f => f.FactType is "email" or "phone").ToList();
        if (contactFacts.Count > 0)
        {
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.ContactExtracted,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.ContactExtraction,
                $"Extracted {contactFacts.Count} public contact(s) from {discovered.Url}",
                url: discovered.Url,
                domain: discovered.NormalizedDomain,
                relatedEntityId: page.Id,
                relatedEntityKind: "crawledPage",
                data: new { contacts = contactFacts.Select(f => f.FactType).ToList() },
                provider: Name,
                cancellationToken: cancellationToken);
        }

        return page;
    }

    private async Task PersistFailedPageAsync(
        ResearchRun run,
        DiscoveredUrl discovered,
        SafeHttpFetchResult fetch,
        CancellationToken cancellationToken)
    {
        _db.CrawledPages.Add(new CrawledPage
        {
            ResearchRunId = run.Id,
            DiscoveredUrlId = discovered.Id,
            ProjectId = run.ProjectId,
            Url = discovered.Url,
            NormalizedUrl = discovered.NormalizedUrl,
            NormalizedDomain = discovered.NormalizedDomain,
            HttpStatusCode = fetch.StatusCode,
            ContentType = fetch.ContentType,
            ContentLengthBytes = fetch.ContentLengthBytes,
            FinalUrl = fetch.FinalUrl?.ToString(),
            RobotsAllowed = !fetch.BlockedByRobots,
            BlockedBySsrf = fetch.BlockedBySsrf,
            WasRedirected = fetch.WasRedirected,
            RedirectCount = fetch.RedirectCount,
            DurationMs = fetch.DurationMs,
            Error = fetch.Error,
            CrawlDepth = discovered.Depth,
            CrawledAtUtc = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordSourceEligibilityEventsAsync(
        Guid runId,
        AutomaticDiscoveryResult discoveryResult,
        CancellationToken cancellationToken)
    {
        foreach (var decision in discoveryResult.SourceEligibilityDecisions)
        {
            if (decision.State is SourceEligibilityState.Eligible
                or SourceEligibilityState.EligibleSecondary
                or SourceEligibilityState.Ineligible)
            {
                await RecordEventAsync(
                    runId,
                    ResearchRunEventType.SourceEligibilityEvaluated,
                    decision.State == SourceEligibilityState.Ineligible
                        ? ResearchRunEventSeverity.Info
                        : ResearchRunEventSeverity.Info,
                    ResearchPipelineStage.Discovery,
                    $"{decision.SourceName}: {decision.State} — {decision.Explanation}",
                    cancellationToken,
                    url: decision.SourceUrl,
                    provider: Name,
                    data: new
                    {
                        sourceName = decision.SourceName,
                        sourceUrl = decision.SourceUrl,
                        sourceKind = decision.SourceKind,
                        state = decision.State.ToString(),
                        reasonCode = decision.ReasonCode,
                        explanation = decision.Explanation,
                        countryMatched = decision.CountryMatched,
                        industryMatched = decision.IndustryMatched,
                        matchedIndustryEvidence = decision.MatchedIndustryEvidence,
                        countsAsRelevantIndustrySource = decision.CountsAsRelevantIndustrySource,
                        canRevealTargetOrganizations = decision.CanRevealTargetOrganizations,
                        expectedDiscoveryValue = decision.ExpectedDiscoveryValue
                    });
            }
        }

        await RecordEventAsync(
            runId,
            ResearchRunEventType.DiscoveryCoverageDecision,
            ResearchRunEventSeverity.Info,
            ResearchPipelineStage.Discovery,
            discoveryResult.Summary ?? "Discovery coverage evaluated.",
            cancellationToken,
            provider: Name,
            data: new
            {
                stopReason = discoveryResult.StopReason.ToString(),
                relevantIndustrySources = discoveryResult.RelevantIndustrySourcesAdmitted,
                secondaryRegistries = discoveryResult.SecondaryRegistrySourcesAdmitted,
                candidateCount = discoveryResult.Candidates.Count,
                uniqueDomains = discoveryResult.UniqueDomains
            });
    }

    private async Task RecordEventAsync(
        Guid runId,
        ResearchRunEventType type,
        ResearchRunEventSeverity severity,
        ResearchPipelineStage stage,
        string? message,
        CancellationToken cancellationToken,
        string? url = null,
        string? domain = null,
        int? durationMs = null,
        int? httpStatusCode = null,
        Guid? relatedEntityId = null,
        string? relatedEntityKind = null,
        object? data = null,
        string? provider = null)
    {
        await _events.RecordAsync(
            new ResearchEventRecordRequest
            {
                ResearchRunId = runId,
                EventType = type,
                Severity = severity,
                Stage = stage,
                Message = message,
                Url = url,
                Domain = domain,
                DurationMs = durationMs,
                HttpStatusCode = httpStatusCode,
                RelatedEntityId = relatedEntityId,
                RelatedEntityKind = relatedEntityKind,
                Data = data,
                Provider = provider
            },
            cancellationToken);
    }

    private static ResearchRunEventType ResolveFetchFailureEventType(FetchFailureClassification failure)
        => failure.Kind switch
        {
            FetchFailureKind.RobotsDisallowed => ResearchRunEventType.RobotsDenied,
            FetchFailureKind.SsrfBlocked => ResearchRunEventType.SsrfBlocked,
            FetchFailureKind.Timeout => ResearchRunEventType.TimeoutOccurred,
            FetchFailureKind.RateLimited => ResearchRunEventType.RateLimited,
            _ => ResearchRunEventType.FetchFailed
        };

    private static void RecordFetchFailure(
        ResearchDiscoveryState state,
        string domain,
        FetchFailureClassification failure)
    {
        if (state.FetchFailureReasons.TryGetValue(failure.ReasonCode, out var count))
        {
            state.FetchFailureReasons[failure.ReasonCode] = count + 1;
        }
        else
        {
            state.FetchFailureReasons[failure.ReasonCode] = 1;
        }

        if (!failure.HostShouldBeAvoided || string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        if (state.HostFailureCounts.TryGetValue(domain, out var hostCount))
        {
            state.HostFailureCounts[domain] = hostCount + 1;
        }
        else
        {
            state.HostFailureCounts[domain] = 1;
        }
    }

    private static void MarkFailed(DiscoveredUrl discovered, string error)
    {
        discovered.Status = DiscoveredUrlStatus.Failed;
        discovered.LastError = error;
        discovered.ProcessedAtUtc = DateTimeOffset.UtcNow;
        discovered.RetryCount++;
    }

    private async Task CompleteWhenNoEligiblePendingAsync(
        ProspectSourceContext context,
        ResearchRun run,
        DiscoveryBudget budget,
        int pendingCount,
        CancellationToken cancellationToken)
    {
        context.DiscoveryState.CurrentInvestigationActivity = pendingCount > 0
            ? "blocked_by_limitation"
            : "finished";
        context.DiscoveryState.LastBudgetStopReason = pendingCount > 0
            ? "fetch.no_eligible_pending"
            : "fetch.queue_empty";
        context.DiscoveryState.UsefulUrlsLeftPendingAtStop = pendingCount;
        context.DiscoveryState.MoreEligibleWorkRemained = false;
        context.DiscoveryState.EligiblePendingUrls = 0;
        if (pendingCount > 0)
        {
            context.DiscoveryState.DeferredUrls = pendingCount;
            context.DiscoveryState.LimitsExhausted = true;
            context.DiscoveryState.LimitsExhaustedReason ??=
                "No eligible pending URLs remain under current per-host budgets or failure avoidance rules.";
        }

        context.DiscoveryState.CompletedSourceNames.Add(Name);
        context.DiscoveryState.BudgetRemaining = Math.Max(
            0,
            budget.MaxFetchesPerRun - context.DiscoveryState.FetchBudgetConsumed);
        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.StageCompleted,
            ResearchRunEventSeverity.Info,
            ResearchPipelineStage.RunLifecycle,
            context.DiscoveryState.LastBudgetStopReason,
            provider: Name,
            data: new
            {
                reasonCode = context.DiscoveryState.LastBudgetStopReason,
                pagesFetched = context.DiscoveryState.PagesFetched,
                fetchBudget = budget.MaxFetchesPerRun,
                pendingCount
            },
            cancellationToken: cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void CaptureTopPendingDiagnostics(
        ResearchDiscoveryState state,
        List<CrawlScheduleItem> scheduled)
    {
        state.TopPendingUrls = scheduled
            .Take(10)
            .Select(x => new PendingUrlDiagnostic
            {
                Url = x.Url.Url,
                Priority = x.Priority.Total,
                PagePurpose = x.PagePurpose.ToString(),
                Host = x.Domain,
                SourceType = x.Url.SourceType.ToString(),
                Reason = x.Priority.Explanation
            })
            .ToList();
    }

    private static string ResolveInvestigationActivity(
        DiscoveredUrl discovered,
        UrlPagePurpose pagePurpose,
        bool isDirectorySeed)
    {
        if (isDirectorySeed || pagePurpose == UrlPagePurpose.DirectoryListing)
        {
            return "exploring_directory";
        }

        if (pagePurpose == UrlPagePurpose.Contact)
        {
            return "looking_for_contacts";
        }

        if (discovered.SourceType == DiscoverySourceType.WebSearch)
        {
            return "searching_for_sources";
        }

        if (pagePurpose is UrlPagePurpose.About or UrlPagePurpose.Team or UrlPagePurpose.Location)
        {
            return "verifying_evidence";
        }

        return "investigating_organization_website";
    }

    private static bool ShouldSkipAsProspectSeed(DiscoveredUrl discovered)
    {
        if (discovered.SourceType is DiscoverySourceType.DirectoryCatalog
            or DiscoverySourceType.IndustryAssociation
            or DiscoverySourceType.WebArchive)
        {
            return true;
        }

        return SystemDirectoryCatalog.IsSeedOrDirectoryHost(discovered.NormalizedDomain ?? discovered.Url);
    }

    private static Dictionary<string, int> BuildInvestigationHostBonuses(ResearchDiscoveryState state)
    {
        var bonuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in state.DomainInvestigationPlans)
        {
            var bonus = WebsiteInvestigationEngine.ResolveAdaptiveHostBonus(pair.Value);
            if (bonus > 0)
            {
                bonuses[pair.Key] = bonus;
            }
        }

        return bonuses;
    }

    private static int ResolveMaxPerDomainInBatch(int maxPagesPerDomain, ResearchDiscoveryState state)
    {
        var hasHighYield = state.DomainInvestigationPlans.Values.Any(x => x.IsHighYield);
        var upper = hasHighYield ? 8 : 5;
        return Math.Clamp(maxPagesPerDomain, 1, upper);
    }

    private static bool LooksLikeDirectoryChildPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2;
    }

    private async Task QueueInvestigationFollowUpsAsync(
        ResearchRun run,
        DiscoveredUrl parent,
        PageInvestigationResult investigation,
        IReadOnlyList<string> industries,
        IReadOnlyList<string> countries,
        int maxDepth,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        if (investigation.FollowUps.Count == 0 || parent.Depth >= maxDepth)
        {
            return;
        }

        var scheduleStartedAt = DateTimeOffset.UtcNow;
        var candidates = new List<DiscoveredUrlCandidate>();
        foreach (var followUp in investigation.FollowUps.Take(30))
        {
            candidates.Add(new DiscoveredUrlCandidate
            {
                Url = followUp.Url,
                SourceType = DiscoverySourceType.LinkExpansion,
                SourceName = followUp.ReasonCode,
                DiscoveryReasonCode = followUp.ReasonCode,
                ParentDiscoveredUrlId = parent.Id,
                Depth = parent.Depth + 1,
                PriorityBoost = followUp.PriorityBoost
            });
        }

        if (candidates.Count == 0)
        {
            return;
        }

        await PersistDiscoveredUrlsAsync(
            run,
            candidates,
            cancellationToken,
            industries,
            countries,
            state);
        state.RecordStageDuration(
            "queueSchedule",
            (long)(DateTimeOffset.UtcNow - scheduleStartedAt).TotalMilliseconds);

        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.InvestigationFollowUpQueued,
            ResearchRunEventSeverity.Info,
            ResearchPipelineStage.Queue,
            $"Queued {candidates.Count} investigation follow-up URL(s) from {parent.Url}.",
            url: parent.Url,
            domain: parent.NormalizedDomain,
            provider: Name,
            data: new
            {
                reasonCode = "investigation.followups_queued",
                count = candidates.Count,
                officialDomains = investigation.OfficialDomainsFound,
                profiles = investigation.ProfileLinksFound,
                summary = investigation.Summary
            },
            cancellationToken: cancellationToken);
    }

    private async Task PersistInvestigationEvidenceAsync(
        ResearchRun run,
        CrawledPage page,
        PageInvestigationResult investigation,
        CancellationToken cancellationToken)
    {
        if (investigation.EvidenceFacts.Count == 0)
        {
            return;
        }

        foreach (var fact in investigation.EvidenceFacts.Take(40))
        {
            _db.ResearchEvidenceItems.Add(new ResearchEvidence
            {
                ProjectId = run.ProjectId,
                ResearchRunId = run.Id,
                CrawledPageId = page.Id,
                FactType = fact.FactType,
                FactValue = fact.FactValue,
                ConfidenceKind = fact.ConfidenceKind,
                SourceUrl = fact.SourceUrl ?? page.FinalUrl ?? page.Url,
                Notes = fact.ExtractionMethod,
                FirstDiscoveredAtUtc = DateTimeOffset.UtcNow,
                LastVerifiedAtUtc = DateTimeOffset.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordInvestigationEventsAsync(
        ResearchRun run,
        DiscoveredUrl discovered,
        PageInvestigationResult investigation,
        CancellationToken cancellationToken)
    {
        if (investigation.PageKind == InvestigationPageKind.Category
            || investigation.CategoryLinksFound > 0)
        {
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.DirectoryCategoryDiscovered,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.Parse,
                $"Discovered directory category structure on {discovered.Url}.",
                url: discovered.Url,
                domain: discovered.NormalizedDomain,
                provider: Name,
                data: new
                {
                    pageKind = investigation.PageKind.ToString(),
                    categoryLinks = investigation.CategoryLinksFound,
                    summary = investigation.Summary
                },
                cancellationToken: cancellationToken);
        }

        if (investigation.ProfileLinksFound > 0 || investigation.Listings.Count > 0)
        {
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.OrganizationProfilesDiscovered,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.CompanyExtraction,
                $"Found {Math.Max(investigation.ProfileLinksFound, investigation.Listings.Count)} organization profile signal(s).",
                url: discovered.Url,
                domain: discovered.NormalizedDomain,
                provider: Name,
                data: new
                {
                    profiles = investigation.ProfileLinksFound,
                    listings = investigation.Listings.Count
                },
                cancellationToken: cancellationToken);
        }

        foreach (var listing in investigation.Listings
                     .Where(x => !CompanyIdentityResolver.IsGenericName(x.Name) && x.Confidence >= 55)
                     .Take(10))
        {
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.ListingOrganizationExtracted,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.CompanyExtraction,
                $"Extracted organization '{listing.Name}' from listing.",
                url: listing.ProfileUrl ?? discovered.Url,
                domain: discovered.NormalizedDomain,
                provider: Name,
                data: new
                {
                    name = listing.Name,
                    officialWebsite = listing.OfficialWebsiteUrl,
                    hasEmail = !string.IsNullOrWhiteSpace(listing.Email),
                    hasPhone = !string.IsNullOrWhiteSpace(listing.Phone),
                    method = listing.ExtractionMethod,
                    confidence = listing.Confidence
                },
                cancellationToken: cancellationToken);
        }

        foreach (var followUp in investigation.FollowUps.Where(x => x.IsExternalOfficialDomain).Take(10))
        {
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.OfficialWebsiteQueued,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.Queue,
                followUp.Explanation,
                url: followUp.Url,
                domain: UrlCanonicalizer.ExtractDomain(followUp.Url),
                provider: Name,
                data: new
                {
                    reasonCode = followUp.ReasonCode,
                    organizationKey = followUp.OrganizationKey
                },
                cancellationToken: cancellationToken);
        }

        foreach (var rejected in investigation.EvidenceFacts
                     .Where(x => string.Equals(x.FactType, "listingEmailRejected", StringComparison.OrdinalIgnoreCase))
                     .Take(5))
        {
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.ContactAttributionRejected,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.ContactExtraction,
                $"Rejected email attribution for '{rejected.FactValue}'.",
                url: rejected.SourceUrl ?? discovered.Url,
                domain: discovered.NormalizedDomain,
                provider: Name,
                data: new
                {
                    email = rejected.FactValue,
                    reasonCode = rejected.ExtractionMethod
                },
                cancellationToken: cancellationToken);
        }

        if (investigation.OrganizationCandidates.Count > 0)
        {
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.EvidenceMerged,
                ResearchRunEventSeverity.Info,
                ResearchPipelineStage.ProspectMerge,
                $"Merged evidence into {investigation.OrganizationCandidates.Count} organization candidate(s).",
                url: discovered.Url,
                domain: discovered.NormalizedDomain,
                provider: Name,
                data: new { count = investigation.OrganizationCandidates.Count },
                cancellationToken: cancellationToken);
        }
    }

    private static void RememberHostOrganization(
        ResearchDiscoveryState state,
        string domain,
        ProspectCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(candidate.CompanyName))
        {
            return;
        }

        var existingConfidence = state.HostOrganizationConfidence.TryGetValue(domain, out var prior)
            ? prior
            : 0;
        if (candidate.NameConfidence >= existingConfidence)
        {
            state.HostOrganizationNames[domain] = candidate.CompanyName;
            state.HostOrganizationConfidence[domain] = candidate.NameConfidence;
        }

        if (candidate.NameConfidence >= 55)
        {
            state.StrongOrganizationHosts.Add(domain);
        }
    }

    private static object ExplainBuildCandidateRejection(
        ProspectSourceContext context,
        DiscoveredUrl discovered,
        ExtractedPageData extracted,
        CrawledPage page)
    {
        var host = discovered.NormalizedDomain ?? UrlCanonicalizer.ExtractDomain(discovered.Url);
        var pagePurpose = UrlAdmissionPolicy.ClassifyPagePurpose(discovered.Url);
        var priorName = host is not null
                        && context.DiscoveryState.HostOrganizationNames.TryGetValue(host, out var remembered)
            ? remembered
            : null;
        var websiteHint = host is null ? discovered.Url : "https://" + host;
        var probeAttribution = ContactAttributionHelper.AttributeBest(
            extracted.Emails,
            page.FinalUrl ?? discovered.Url,
            websiteHint,
            pageIsDirectory: false);
        var identity = CompanyIdentityResolver.Resolve(
            extracted.Title,
            extracted.Excerpt,
            discovered.NormalizedDomain,
            page.FinalUrl ?? discovered.Url,
            new CompanyIdentityEvidence
            {
                StructuredOrganizationName = extracted.OrganizationName,
                OpenGraphSiteName = extracted.OpenGraphSiteName,
                OpenGraphTitle = extracted.OpenGraphTitle,
                PriorOrganizationName = priorName,
                PageHeading = extracted.PageHeading,
                PagePurpose = pagePurpose,
                HasSameDomainEmail = probeAttribution.State == ContactAttributionState.SameDomain,
                CorroboratingFirstPartyPages = priorName is null ? 0 : 1
            });
        var entity = EntityClassifier.Classify(
            identity.CompanyName ?? extracted.Title,
            extracted.Title,
            extracted.Excerpt,
            discovered.NormalizedDomain ?? discovered.Url,
            discovered.SourceType);
        return new
        {
            reasonCode = "prospect.identity_or_entity_rejected",
            identityReason = identity.ReasonCode,
            identityConfidence = identity.NameConfidence,
            identityName = identity.CompanyName,
            identityEvidence = identity.EvidenceCodes,
            entityType = entity.EntityType.ToString(),
            entityReason = entity.ReasonCode,
            pagePurpose = pagePurpose.ToString(),
            hostCountryMatch = CountryScopeHelper.HostMatchesCountries(host, context.Countries),
            emailsFound = extracted.Emails.Count,
            contactReason = probeAttribution.ReasonCode
        };
    }

    private static ProspectCandidate? BuildCandidate(
        ProspectSourceContext context,
        DiscoveredUrl discovered,
        ExtractedPageData extracted,
        CrawledPage page)
    {
        if (extracted.IsErrorPage || extracted.IsNonHtmlAsset)
        {
            return null;
        }

        var host = discovered.NormalizedDomain ?? UrlCanonicalizer.ExtractDomain(discovered.Url);
        if (!CountryScopeHelper.HostMatchesCountries(host, context.Countries))
        {
            return null;
        }

        if (Uri.TryCreate(discovered.Url, UriKind.Absolute, out var candidateUri)
            && CountryScopeHelper.PathLooksOutOfCountryScope(candidateUri.AbsolutePath, context.Countries))
        {
            return null;
        }

        var pagePurpose = !string.IsNullOrWhiteSpace(discovered.PagePurpose)
                          && Enum.TryParse<UrlPagePurpose>(discovered.PagePurpose, true, out var storedPurpose)
            ? storedPurpose
            : UrlAdmissionPolicy.ClassifyPagePurpose(discovered.Url);
        var priorName = host is not null
                        && context.DiscoveryState.HostOrganizationNames.TryGetValue(host, out var remembered)
            ? remembered
            : null;
        var websiteHint = string.IsNullOrWhiteSpace(host)
            ? discovered.Url
            : "https://" + host;
        var pageIsDirectoryProbe = SystemDirectoryCatalog.IsSeedOrDirectoryHost(discovered.NormalizedDomain ?? discovered.Url);
        var attributionProbe = ContactAttributionHelper.AttributeBest(
            extracted.Emails,
            page.FinalUrl ?? discovered.Url,
            websiteHint,
            pageIsDirectoryProbe);

        var identity = CompanyIdentityResolver.Resolve(
            extracted.Title,
            extracted.Excerpt,
            discovered.NormalizedDomain,
            page.FinalUrl ?? discovered.Url,
            new CompanyIdentityEvidence
            {
                StructuredOrganizationName = extracted.OrganizationName,
                OpenGraphSiteName = extracted.OpenGraphSiteName,
                OpenGraphTitle = extracted.OpenGraphTitle,
                PriorOrganizationName = priorName,
                PageHeading = extracted.PageHeading,
                PagePurpose = pagePurpose,
                HasSameDomainEmail = attributionProbe.State == ContactAttributionState.SameDomain,
                CorroboratingFirstPartyPages = priorName is null ? 0 : 1
            });
        if (!identity.IsAcceptable || string.IsNullOrWhiteSpace(identity.CompanyName))
        {
            return null;
        }

        var entity = EntityClassifier.Classify(
            identity.CompanyName,
            extracted.Title,
            extracted.Excerpt,
            discovered.NormalizedDomain ?? discovered.Url,
            discovered.SourceType);
        if (!EntityClassifier.IsAllowedTarget(entity.EntityType, ResearchEntityType.TargetBusiness))
        {
            return null;
        }

        var website = string.IsNullOrWhiteSpace(identity.OfficialDomain)
            ? (discovered.NormalizedDomain is null ? discovered.Url : "https://" + discovered.NormalizedDomain)
            : "https://" + identity.OfficialDomain;
        var evidenceUrl = page.FinalUrl ?? discovered.Url;
        var pageIsDirectory = entity.EntityType == ResearchEntityType.DirectoryAggregator
                              || SystemDirectoryCatalog.IsSeedOrDirectoryHost(discovered.NormalizedDomain ?? discovered.Url);
        var attribution = ContactAttributionHelper.AttributeBest(
            extracted.Emails,
            evidenceUrl,
            website,
            pageIsDirectory);
        var email = attribution.State == ContactAttributionState.SameDomain
            ? attribution.Email
            : null;
        var attributedEmailContacts = new List<Domain.Services.AttributedContact>();
        var attributedEmails = new List<string>();
        foreach (var candidateEmail in extracted.Emails.Take(12))
        {
            var result = ContactAttributionHelper.Attribute(candidateEmail, evidenceUrl, website, pageIsDirectory);
            if (result.State != ContactAttributionState.SameDomain)
            {
                continue;
            }

            attributedEmailContacts.Add(new Domain.Services.AttributedContact
            {
                Value = candidateEmail,
                Channel = "email",
                SourceUrl = evidenceUrl,
                DiscoveredAtUtc = DateTimeOffset.UtcNow,
                VerificationStatus = "same_domain",
                Context = extracted.Title ?? "official_page",
                AttributionReasonCode = result.ReasonCode,
                NormalizedValue = candidateEmail.ToLowerInvariant(),
                PageTitle = extracted.Title,
                SurroundingText = extracted.Excerpt.Length > 240 ? extracted.Excerpt[..240] : extracted.Excerpt,
                Confidence = result.Confidence,
                ReviewStatus = "accepted"
            });
            if (!string.Equals(candidateEmail, email, StringComparison.OrdinalIgnoreCase))
            {
                attributedEmails.Add(candidateEmail);
            }
        }

        var phone = extracted.Phones.FirstOrDefault();
        var attributedPhoneContacts = extracted.Phones
            .Take(10)
            .Select(p => new Domain.Services.AttributedContact
            {
                Value = p,
                Channel = "phone",
                SourceUrl = evidenceUrl,
                DiscoveredAtUtc = DateTimeOffset.UtcNow,
                VerificationStatus = "extracted",
                Context = extracted.Title ?? "official_page",
                AttributionReasonCode = "contact.page_phone",
                NormalizedValue = new string(p.Where(char.IsDigit).ToArray()),
                PageTitle = extracted.Title,
                SurroundingText = extracted.Excerpt.Length > 240 ? extracted.Excerpt[..240] : extracted.Excerpt,
                Confidence = 50,
                ReviewStatus = "unreviewed"
            })
            .ToList();
        var inferredCountry = CountryScopeHelper.InferCountryFromHost(host)
                              ?? CountryScopeHelper.InferCountryFromPath(
                                  Uri.TryCreate(evidenceUrl, UriKind.Absolute, out var evidenceUri)
                                      ? evidenceUri.AbsolutePath
                                      : null);
        var countryEvidenceSource = inferredCountry is null
            ? null
            : CountryScopeHelper.InferCountryFromHost(host) is not null
                ? "host_tld"
                : "source_path";
        var industry = ResolveIndustryFromEvidence(
            extracted,
            identity.CompanyName,
            context.Industries,
            context.ExcludedIndustries);

        return new ProspectCandidate
        {
            CompanyName = identity.CompanyName,
            Website = website,
            OfficialDomain = identity.OfficialDomain,
            Country = inferredCountry,
            CountryEvidenceSource = countryEvidenceSource,
            CountryEvidenceSummary = inferredCountry is null
                ? null
                : $"{countryEvidenceSource}:{inferredCountry}",
            Industry = industry,
            CompanyDescription = extracted.Excerpt.Length > 500
                ? extracted.Excerpt[..500]
                : extracted.Excerpt,
            Email = email,
            AdditionalEmails = attributedEmails,
            AttributedEmails = attributedEmailContacts,
            Phone = phone,
            AdditionalPhones = extracted.Phones.Skip(1).Take(10).ToList(),
            AttributedPhones = attributedPhoneContacts,
            WhatsApp = extracted.WhatsApps.FirstOrDefault(),
            SourceUrl = evidenceUrl,
            AdditionalSourceUrls = extracted.SameOriginLinks.Take(5).ToList(),
            SourceType = ProspectSourceType.Website,
            SourceName = discovered.SourceName ?? "Independent crawler",
            ResearchNotes =
                $"entity={entity.ReasonCode}; identity={identity.ReasonCode}; evidence={string.Join(',', identity.EvidenceCodes)}; contact={attribution.ReasonCode}; page='{extracted.Title ?? discovered.Url}'.",
            EntityType = entity.EntityType,
            EntityTypeConfidence = entity.Confidence,
            EntityTypeReasonCode = entity.ReasonCode,
            NameConfidence = identity.NameConfidence,
            NameReasonCode = identity.ReasonCode,
            ContactEvidenceUrl = attribution.EvidenceUrl,
            ContactPageDomain = attribution.PageDomain,
            ContactAttributionState = attribution.State,
            ContactAttributionConfidence = attribution.Confidence,
            ContactAttributionReasonCode = attribution.ReasonCode
        };
    }

    private static string? ResolveIndustryFromEvidence(
        ExtractedPageData extracted,
        string companyName,
        List<string> targetIndustries,
        List<string> excludedIndustries)
    {
        var classification = IndustryEvidenceClassifier.Classify(
            companyName,
            extracted.Title,
            extracted.Excerpt,
            extracted.PageHeading,
            targetIndustries,
            excludedIndustries);
        if (classification.IsExcluded || !classification.MatchedApprovedIndustry)
        {
            return classification.MatchedApprovedIndustry ? classification.AssignedIndustry : null;
        }

        return classification.AssignedIndustry;
    }
}
