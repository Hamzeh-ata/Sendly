using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Application.Services;

public class ResearchProcessor
{
    private readonly ISendlyDbContext _db;
    private readonly IEnumerable<IProspectSource> _sources;
    private readonly IAiResearchService _ai;
    private readonly ProspectDuplicateService _duplicates;
    private readonly ProspectQualificationService _qualificationService;
    private readonly CrawlRetentionService _retentionService;
    private readonly DiscoverySourceRegistryService _discoverySourceRegistry;
    private readonly ResearchCancellationRegistry _cancellationRegistry;
    private readonly IResearchProgressNotifier _progressNotifier;
    private readonly IResearchEventRecorder _events;
    private readonly ResearchOptions _options;
    private readonly ILogger<ResearchProcessor> _logger;
    private readonly ILocalAiHealthService? _localAiHealth;
    private readonly ILocalAiQueryPlanner? _localAiQueryPlanner;
    private readonly AdaptiveResearchPlanner? _adaptivePlanner;

    public ResearchProcessor(
        ISendlyDbContext db,
        IEnumerable<IProspectSource> sources,
        IAiResearchService ai,
        ProspectDuplicateService duplicates,
        ProspectQualificationService qualificationService,
        CrawlRetentionService retentionService,
        DiscoverySourceRegistryService discoverySourceRegistry,
        ResearchCancellationRegistry cancellationRegistry,
        IResearchProgressNotifier progressNotifier,
        IResearchEventRecorder events,
        IOptions<ResearchOptions> options,
        ILogger<ResearchProcessor> logger,
        ILocalAiHealthService? localAiHealth = null,
        ILocalAiQueryPlanner? localAiQueryPlanner = null,
        AdaptiveResearchPlanner? adaptivePlanner = null)
    {
        _localAiHealth = localAiHealth;
        _localAiQueryPlanner = localAiQueryPlanner;
        _adaptivePlanner = adaptivePlanner;
        _db = db;
        _sources = sources;
        _ai = ai;
        _duplicates = duplicates;
        _qualificationService = qualificationService;
        _retentionService = retentionService;
        _discoverySourceRegistry = discoverySourceRegistry;
        _cancellationRegistry = cancellationRegistry;
        _progressNotifier = progressNotifier;
        _events = events;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid researchRunId, CancellationToken stoppingToken)
    {
        var runToken = _cancellationRegistry.GetOrCreate(researchRunId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, runToken);
        var cancellationToken = linked.Token;

        var run = await _db.ResearchRuns.FirstOrDefaultAsync(x => x.Id == researchRunId, cancellationToken);
        if (run is null)
        {
            _cancellationRegistry.Clear(researchRunId);
            return;
        }

        if (ResearchRunOutcomeResolver.IsTerminal(run.Status))
        {
            _cancellationRegistry.Clear(researchRunId);
            return;
        }

        var project = await _db.OutreachProjects
            .FirstOrDefaultAsync(x => x.Id == run.ProjectId, cancellationToken);
        if (project is null)
        {
            run.Status = ResearchRunStatus.Failed;
            run.Error = "Project was not found.";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(run, null, cancellationToken);
            _cancellationRegistry.Clear(researchRunId);
            return;
        }

        project.Keywords = await _db.OutreachProjectKeywords
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.Countries = await _db.OutreachProjectCountries
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.Industries = await _db.OutreachProjectIndustries
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.TargetRoles = await _db.OutreachProjectTargetRoles
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.Templates = await _db.ProjectTemplates
            .Include(x => x.Template)
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);

        run.Status = ResearchRunStatus.Running;
        run.StartedAtUtc = DateTimeOffset.UtcNow;
        run.ProgressPercent = 5;
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(run, project.Name, cancellationToken);
        await RecordLifecycleEventAsync(
            run.Id,
            ResearchRunEventType.Started,
            ResearchRunEventSeverity.Info,
            $"Research run started for {project.Name}",
            new { projectId = project.Id, requested = run.RequestedProspectCount },
            cancellationToken);
        _logger.LogInformation("Research run {ResearchRunId} started", researchRunId);

        var providerFailures = new List<string>();
        var discoveryState = new ResearchDiscoveryState
        {
            StartedAtUtc = run.StartedAtUtc ?? DateTimeOffset.UtcNow
        };

        try
        {
            var context = BuildSourceContext(project, run, discoveryState);
            await InterpretProjectAsync(project, run, context, cancellationToken);
            await EnsureOllamaForRunAsync(project, run, context, discoveryState, cancellationToken);
            context.DiscoveryBatchSize = ResolveDiscoveryBatchSize(run.RequestedProspectCount);
            discoveryState.ConfiguredBatchSize = context.DiscoveryBatchSize;
            discoveryState.RequestedFetchBudget = _options.MaxFetchesPerRun;
            context.ReportProgressAsync = async (state, token) =>
            {
                ApplyDiscoveryStats(run, state);
                run.CompaniesFound = Math.Max(run.CompaniesFound, state.CompaniesIdentified);
                run.ContactsFound = Math.Max(run.ContactsFound, state.CandidatesAfterDeduplication);
                run.ProgressPercent = ResolveProgress(run, state);
                await _db.SaveChangesAsync(token);
                await NotifyAsync(run, project.Name, token);
            };

            if (!_sources.Any(x => x.IsEnabled))
            {
                discoveryState.LimitsExhausted = true;
                discoveryState.LimitsExhaustedReason = "No research providers are enabled.";
            }

            while (ResearchRunContinuationPolicy.ShouldContinueResearch(
                       run,
                       discoveryState,
                       _options.MaxResearchDurationMinutes))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pagesFetchedBefore = discoveryState.PagesFetched;
                var pendingBefore = discoveryState.PagesQueued;
                var batchStartedAt = DateTimeOffset.UtcNow;
                discoveryState.LastHeartbeatAtUtc = DateTimeOffset.UtcNow;
                discoveryState.BatchesStarted++;
                var batch = await DiscoverNextBatchAsync(context, providerFailures, cancellationToken);
                discoveryState.BatchesCompleted++;
                discoveryState.RecordStageDuration(
                    "discoveryBatch",
                    (long)(DateTimeOffset.UtcNow - batchStartedAt).TotalMilliseconds);
                if (batch.Count == 0)
                {
                    if (AreAllSourcesCompleted(discoveryState)
                        && !ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(discoveryState))
                    {
                        discoveryState.MoreEligibleWorkRemained = false;
                        break;
                    }

                    if (discoveryState.LimitsExhausted
                        && !ResearchRunContinuationPolicy.IsSearchOnlyLimitReason(
                            discoveryState.LimitsExhaustedReason)
                        && !ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(discoveryState))
                    {
                        discoveryState.MoreEligibleWorkRemained = false;
                        break;
                    }

                    if (ResearchRunContinuationPolicy.IsSearchDue(discoveryState))
                    {
                        continue;
                    }

                    if (ResearchRunContinuationPolicy.ShouldContinueAfterEmptyProspectBatch(
                            discoveryState,
                            pagesFetchedBefore,
                            pendingBefore))
                    {
                        continue;
                    }

                    discoveryState.MoreEligibleWorkRemained =
                        ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(discoveryState);
                    if (!discoveryState.MoreEligibleWorkRemained)
                    {
                        discoveryState.LimitsExhausted = true;
                        discoveryState.LimitsExhaustedReason ??= ResolveEmptyBatchStopReason(discoveryState);
                    }

                    break;
                }

                discoveryState.UniqueEntitiesAfterDedup = CountDistinctCompanies(batch);
                ApplyDiscoveryStats(run, discoveryState);
                run.CompaniesFound = Math.Max(run.CompaniesFound, discoveryState.CompaniesIdentified);
                run.ContactsFound = Math.Max(run.ContactsFound, run.CandidatesAfterDeduplication);
                run.ProgressPercent = ResolveProgress(run, discoveryState);
                await _db.SaveChangesAsync(cancellationToken);
                await NotifyAsync(run, project.Name, cancellationToken);

                var qualifyStartedAt = DateTimeOffset.UtcNow;
                foreach (var candidate in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (run.QualifiedCount >= run.RequestedProspectCount)
                    {
                        break;
                    }

                    await ProcessCandidateAsync(project, run, candidate, discoveryState, cancellationToken);
                    run.ProgressPercent = ResolveProgress(run, discoveryState);
                    ApplyDiscoveryStats(run, discoveryState);
                    await _db.SaveChangesAsync(cancellationToken);
                    await NotifyAsync(run, project.Name, cancellationToken);
                }

                discoveryState.RecordStageDuration(
                    "qualification",
                    (long)(DateTimeOffset.UtcNow - qualifyStartedAt).TotalMilliseconds);

                if (run.QualifiedCount >= run.RequestedProspectCount)
                {
                    break;
                }

                if (ResearchRunContinuationPolicy.IsDurationExceeded(
                        discoveryState,
                        _options.MaxResearchDurationMinutes))
                {
                    discoveryState.LimitsExhausted = true;
                    discoveryState.LimitsExhaustedReason ??= "Maximum research duration reached";
                    break;
                }
            }

            ApplyDiscoveryStats(run, discoveryState);
            FinalizeRunStatus(run, discoveryState, providerFailures, AreAllSourcesCompleted(discoveryState));
            run.ProgressPercent = 100;
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await _retentionService.PurgeFullTextExcerptsAsync(run.Id, cancellationToken);
            await NotifyAsync(run, project.Name, cancellationToken);
            await RecordLifecycleEventAsync(
                run.Id,
                ResearchRunEventType.Completed,
                run.Status == ResearchRunStatus.CompletedWithFailures
                    ? ResearchRunEventSeverity.Warning
                    : ResearchRunEventSeverity.Info,
                $"Research run finished with status {run.Status}",
                new
                {
                    status = run.Status.ToString(),
                    qualified = run.QualifiedCount,
                    requested = run.RequestedProspectCount,
                    pagesCrawled = run.PagesCrawled,
                    crawlErrors = run.CrawlErrors
                },
                cancellationToken);
            _logger.LogInformation(
                "Research completed {ResearchRunId} qualified={Qualified}/{Target} queries={Queries} results={Results}",
                researchRunId,
                run.QualifiedCount,
                run.RequestedProspectCount,
                run.SearchQueriesExecuted,
                run.SearchResultsReceived);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ApplyDiscoveryStats(run, discoveryState);
            run.Status = ResearchRunStatus.Cancelled;
            run.Warning = "Research run was cancelled.";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await NotifyAsync(run, project.Name, CancellationToken.None);
            await RecordLifecycleEventAsync(
                run.Id,
                ResearchRunEventType.Cancelled,
                ResearchRunEventSeverity.Warning,
                "Research run was cancelled.",
                null,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            ApplyDiscoveryStats(run, discoveryState);
            run.Status = ResearchRunStatus.Failed;
            run.Error = IsTimeoutCancellation(ex)
                ? "Research failed because a provider or website request timed out. The run was not cancelled by the user."
                : ex.Message;
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await NotifyAsync(run, project.Name, CancellationToken.None);
            await RecordLifecycleEventAsync(
                run.Id,
                ResearchRunEventType.Error,
                ResearchRunEventSeverity.Critical,
                run.Error,
                new { exceptionType = ex.GetType().Name },
                CancellationToken.None);
            _logger.LogError(ex, "Research failed {ResearchRunId}", researchRunId);
        }
        finally
        {
            _cancellationRegistry.Clear(researchRunId);
        }
    }

    private List<IProspectSource> OrderSourcesForBatch(ResearchDiscoveryState state)
    {
        var sources = _sources.ToList();
        var search = sources.FirstOrDefault(x => string.Equals(
            x.Name,
            ResearchRunContinuationPolicy.WebSearchSourceName,
            StringComparison.OrdinalIgnoreCase));
        var crawler = sources.FirstOrDefault(x => string.Equals(
            x.Name,
            ResearchRunContinuationPolicy.IndependentCrawlerSourceName,
            StringComparison.OrdinalIgnoreCase));
        if (search is null)
        {
            return sources;
        }

        sources.RemoveAll(x =>
            string.Equals(x.Name, ResearchRunContinuationPolicy.WebSearchSourceName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Name, ResearchRunContinuationPolicy.IndependentCrawlerSourceName, StringComparison.OrdinalIgnoreCase));

        var searchExhausted = state.SearchLimitsExhausted
                              || state.CompletedSourceNames.Contains(ResearchRunContinuationPolicy.WebSearchSourceName);
        if (search.IsEnabled && !searchExhausted)
        {
            sources.Insert(0, search);
            if (crawler is not null)
            {
                sources.Insert(1, crawler);
            }

            return sources;
        }

        if (crawler is not null)
        {
            sources.Insert(0, crawler);
        }

        if (search.IsEnabled && !state.CompletedSourceNames.Contains(search.Name))
        {
            sources.Add(search);
        }

        return sources;
    }

    private static bool IsSearchSource(IProspectSource source)
        => string.Equals(
            source.Name,
            ResearchRunContinuationPolicy.WebSearchSourceName,
            StringComparison.OrdinalIgnoreCase);

    private static void TrackSourceBatch(ResearchDiscoveryState state, string sourceName)
    {
        if (string.Equals(
                sourceName,
                ResearchRunContinuationPolicy.WebSearchSourceName,
                StringComparison.OrdinalIgnoreCase))
        {
            state.CrawlBatchesSinceSearch = 0;
            state.SearchBatchesCompleted++;
            return;
        }

        if (string.Equals(
                sourceName,
                ResearchRunContinuationPolicy.IndependentCrawlerSourceName,
                StringComparison.OrdinalIgnoreCase))
        {
            state.CrawlBatchesSinceSearch++;
        }
    }

    private async Task<List<ProspectCandidate>> DiscoverNextBatchAsync(
        ProspectSourceContext context,
        List<string> providerFailures,
        CancellationToken cancellationToken)
    {
        var batch = new List<ProspectCandidate>();
        foreach (var source in OrderSourcesForBatch(context.DiscoveryState))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!source.IsEnabled)
            {
                continue;
            }

            if (context.DiscoveryState.CompletedSourceNames.Contains(source.Name))
            {
                continue;
            }

            try
            {
                var sourceStartedAt = DateTimeOffset.UtcNow;
                var discovered = await source.DiscoverAsync(context, cancellationToken);
                TrackSourceBatch(context.DiscoveryState, source.Name);
                context.DiscoveryState.RecordStageDuration(
                    $"source.{source.Name}",
                    (long)(DateTimeOffset.UtcNow - sourceStartedAt).TotalMilliseconds);
                batch.AddRange(discovered);
                if (IsSearchSource(source) && batch.Count > 0)
                {
                    break;
                }

                if (discovered.Count == 0
                    && !string.Equals(
                        source.Name,
                        ResearchRunContinuationPolicy.WebSearchSourceName,
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        source.Name,
                        ResearchRunContinuationPolicy.IndependentCrawlerSourceName,
                        StringComparison.OrdinalIgnoreCase)
                    && !context.DiscoveryState.CompletedSourceNames.Contains(source.Name))
                {
                    context.DiscoveryState.CompletedSourceNames.Add(source.Name);
                }

                _logger.LogInformation(
                    "Candidate discovered batch from {Source}: {Count}",
                    source.Name,
                    discovered.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                providerFailures.Add($"{source.Name}: {ex.Message}");
                context.DiscoveryState.CompletedSourceNames.Add(source.Name);
                _logger.LogWarning(ex, "Provider failed {Source}", source.Name);
            }
        }

        var deduped = ProspectCandidateDeduplicator.DeduplicateAndMerge(batch);
        context.DiscoveryState.CandidatesAfterDeduplication = Math.Max(
            context.DiscoveryState.CandidatesAfterDeduplication,
            deduped.Count);
        context.DiscoveryState.UniqueEntitiesAfterDedup = Math.Max(
            context.DiscoveryState.UniqueEntitiesAfterDedup,
            CountDistinctCompanies(deduped));
        context.DiscoveryState.UniqueOrganizationCandidates = context.DiscoveryState.UniqueEntitiesAfterDedup;
        return deduped;
    }

    private static string ResolveEmptyBatchStopReason(ResearchDiscoveryState state)
    {
        if (!string.IsNullOrWhiteSpace(state.LastBudgetStopReason))
        {
            return $"Discovery returned no prospect candidates ({state.LastBudgetStopReason}).";
        }

        if (state.UsefulPendingUrls > 0 || state.PagesQueued > 0)
        {
            return "No eligible crawl progress remained under current host/budget constraints.";
        }

        return "No additional candidates could be discovered.";
    }

    private bool AreAllSourcesCompleted(ResearchDiscoveryState state)
    {
        if (state.LimitsExhausted)
        {
            return true;
        }

        var enabled = false;
        foreach (var source in _sources)
        {
            if (!source.IsEnabled)
            {
                continue;
            }

            enabled = true;
            if (!state.CompletedSourceNames.Contains(source.Name))
            {
                return false;
            }
        }

        return enabled;
    }

    private static void FinalizeRunStatus(
        ResearchRun run,
        ResearchDiscoveryState state,
        List<string> providerFailures,
        bool allSourcesCompleted)
    {
        if (state.DiscoveryStopReason == DiscoveryStopReason.None)
        {
            state.DiscoveryStopReason = DiscoveryOutcomeClassifier.Classify(
                state,
                run.QualifiedCount,
                run.RequestedProspectCount);
        }

        var outcome = DiscoveryOutcomeClassifier.Describe(state.DiscoveryStopReason);
        if (run.QualifiedCount >= run.RequestedProspectCount)
        {
            run.Status = providerFailures.Count > 0
                ? ResearchRunStatus.CompletedWithFailures
                : ResearchRunStatus.Completed;
            if (providerFailures.Count > 0)
            {
                run.Warning = string.Join("; ", providerFailures);
            }

            return;
        }

        run.ResearchLimitsReached = state.LimitsExhausted
                                   || (allSourcesCompleted
                                       && !ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(state));
        run.ResearchLimitsReachedReason = ResolveLimitsReachedReason(state, outcome);

        var underCovered = state.ResolveUnderCoveredIndustries();
        var underCoveredText = underCovered.Count == 0
            ? "No under-covered industries."
            : $"Under-covered industries: {string.Join(", ", underCovered.Take(8))}.";
        var summary =
            $"{run.RequestedProspectCount} requested, {run.QualifiedCount} qualified, {state.CompaniesIdentified} organizations, {state.PublicContactsExtracted} contacts. {outcome} {underCoveredText} Next: {state.ResolveNextBestStrategy()}";

        run.Status = ResearchRunOutcomeResolver.Resolve(
            run.QualifiedCount,
            run.RequestedProspectCount,
            state,
            providerFailures.Count,
            allSourcesCompleted);
        run.Warning = providerFailures.Count > 0
            ? $"{summary} Providers: {string.Join("; ", providerFailures)}"
            : summary;
    }

    private static void ApplyDiscoveryStats(ResearchRun run, ResearchDiscoveryState state)
    {
        run.SearchQueriesExecuted = state.SearchQueriesExecuted;
        run.SearchResultsReceived = state.SearchResultsReceived;
        run.CandidatesDiscovered = state.CandidatesDiscovered;
        run.CandidatesAfterDeduplication = Math.Max(run.CandidatesAfterDeduplication, state.CandidatesAfterDeduplication);
        run.CandidatesValidated = state.CandidatesValidated;
        run.ProspectsSaved = state.ProspectsSaved;
        run.ProspectsSkippedAsDuplicates = state.ProspectsSkippedAsDuplicates;
        run.ResearchLimitsReached = state.LimitsExhausted;
        run.ResearchLimitsReachedReason = ResolveLimitsReachedReason(state, state.LimitsExhaustedReason);
        run.RejectionReasonsJson = state.SerializeRejectionReasons();
        run.SearchProviderDiagnosticsJson = state.SerializeProviderDiagnostics();
    }

    private static string? ResolveLimitsReachedReason(ResearchDiscoveryState state, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(state.LimitsExhaustedReason)
            && !ResearchRunContinuationPolicy.IsSearchOnlyLimitReason(state.LimitsExhaustedReason))
        {
            return state.LimitsExhaustedReason;
        }

        if (state.SearchLimitsExhausted
            && !string.IsNullOrWhiteSpace(state.SearchLimitsExhaustedReason)
            && !ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(state))
        {
            return state.SearchLimitsExhaustedReason;
        }

        if (!string.IsNullOrWhiteSpace(state.LastBudgetStopReason))
        {
            return state.LastBudgetStopReason;
        }

        return fallback ?? state.SearchLimitsExhaustedReason;
    }

    private int ResolveDiscoveryBatchSize(int requestedCount)
    {
        var oversample = Math.Clamp(_options.DiscoveryOversampleFactor, 1, 10);
        var target = Math.Max(8, Math.Min(15, requestedCount / 3));
        var batchSize = Math.Clamp(target * Math.Max(1, oversample / 2), 8, 20);
        return batchSize;
    }

    private static int ResolveProgress(ResearchRun run, ResearchDiscoveryState? state = null)
    {
        if (run.RequestedProspectCount <= 0)
        {
            return 5;
        }

        var qualifiedRatio = run.QualifiedCount / (double)run.RequestedProspectCount;
        var qualifiedProgress = (int)Math.Floor(qualifiedRatio * 55);

        var queries = state?.SearchQueriesExecuted ?? run.SearchQueriesExecuted;
        var maxQueries = Math.Max(1, queries > 0 ? Math.Max(queries, 10) : 50);
        var queryProgress = (int)Math.Floor(Math.Min(queries / (double)maxQueries, 1.0) * 15);

        var discovered = state?.CandidatesDiscovered ?? run.CandidatesDiscovered;
        var discoveryTarget = Math.Max(run.RequestedProspectCount, 1);
        var discoveryProgress = (int)Math.Floor(Math.Min(discovered / (double)(discoveryTarget * 2), 1.0) * 15);

        var validated = state?.CandidatesValidated ?? run.CandidatesValidated;
        var validatedProgress = (int)Math.Floor(Math.Min(validated / (double)discoveryTarget, 1.0) * 10);

        return Math.Clamp(5 + queryProgress + discoveryProgress + validatedProgress + qualifiedProgress, 5, 95);
    }

    private async Task NotifyAsync(ResearchRun run, string? projectName, CancellationToken cancellationToken)
    {
        var prospects = await _db.Prospects.AsNoTracking()
            .Where(x => x.ResearchRunId == run.Id)
            .ToListAsync(cancellationToken);
        prospects = prospects
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToList();
        var detail = ResearchRunMapper.MapDetail(run, projectName, prospects);
        await _progressNotifier.NotifyProgressAsync(detail, cancellationToken);
    }

    private async Task EnsureOllamaForRunAsync(
        OutreachProject project,
        ResearchRun run,
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        if (_localAiHealth is null)
        {
            return;
        }

        var health = await _localAiHealth.EnsureRunningAsync(cancellationToken);
        state.OllamaChecked = true;
        state.OllamaReachable = health.IsReachable;
        state.OllamaModelAvailable = health.ModelAvailable;
        state.OllamaModel = health.Model;
        var queriesGenerated = 0;
        if (health.ModelAvailable && _localAiQueryPlanner is not null)
        {
            queriesGenerated = await GenerateOllamaQueriesForRunAsync(context, state, cancellationToken);
        }

        state.OllamaQueriesGenerated = queriesGenerated;
        var severity = health.ModelAvailable
            ? ResearchRunEventSeverity.Info
            : ResearchRunEventSeverity.Warning;
        var message = health.ModelAvailable
            ? $"Ollama running for this research run: model {health.Model} at {health.BaseUrl} ({queriesGenerated} AI search queries generated)."
            : $"Ollama could not be made available for this research run: {health.ErrorSummary ?? "unknown error"} Deterministic research continues.";
        _logger.LogInformation(
            "Research run {ResearchRunId} Ollama check: reachable={Reachable} modelAvailable={ModelAvailable} model={Model} aiQueries={Queries}",
            run.Id,
            health.IsReachable,
            health.ModelAvailable,
            health.Model,
            queriesGenerated);
        await RecordLifecycleEventAsync(
            run.Id,
            ResearchRunEventType.SearchProviderHealthChecked,
            severity,
            message,
            new
            {
                provider = "Ollama",
                model = health.Model,
                baseUrl = health.BaseUrl,
                reachable = health.IsReachable,
                modelAvailable = health.ModelAvailable,
                availableModels = health.AvailableModels,
                aiQueriesGenerated = queriesGenerated,
                error = health.ErrorSummary
            },
            cancellationToken,
            ResearchPipelineStage.Discovery);
    }

    private async Task<int> GenerateOllamaQueriesForRunAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        if (_adaptivePlanner is not null)
        {
            return await GenerateAdaptivePlanForRunAsync(context, state, cancellationToken);
        }

        if (_localAiQueryPlanner is null)
        {
            return 0;
        }

        try
        {
            var spec = ProjectEligibilityPolicy.RequireSpec(context);
            var queries = await _localAiQueryPlanner.GenerateSearchQueriesAsync(
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
                    MaxQueries = 12,
                    HardConstraintsSummary =
                        $"Countries={string.Join('|', spec.TargetCountries)}; Industries={string.Join('|', spec.ApprovedIndustries)}; Exclusions={string.Join('|', spec.ExcludedIndustries)}",
                    CoverageSummary = $"qualified so far in this run; industries planned: {string.Join(", ", context.Industries)}",
                    RejectionSummary = state.RejectionReasons.Count == 0
                        ? null
                        : string.Join(", ", state.RejectionReasons.Select(x => $"{x.Key}={x.Value}")),
                    UnderCoveredIndustries = state.ResolveUnderCoveredIndustries(),
                    UnderCoveredLocations = context.Locations
                },
                cancellationToken);
            state.OllamaCallsUsed++;
            var seen = new HashSet<string>(state.GeneratedQueries, StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var query in queries ?? [])
            {
                var trimmed = query?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
                {
                    continue;
                }

                var planned = new PlannedSearchQuery
                {
                    Query = trimmed,
                    Industry = context.Industries.FirstOrDefault() ?? "business",
                    Country = context.Countries.FirstOrDefault(),
                    Location = context.Locations.FirstOrDefault(),
                    Intent = SearchQueryIntents.DiscoverBusiness,
                    Purpose = "initial_ai_plan",
                    ExpectedResultType = "operating_business"
                };
                var validation = ProjectEligibilityPolicy.ValidateQuery(
                    planned.Query,
                    spec,
                    planned.Industry,
                    planned.Country);
                if (!validation.IsValid)
                {
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
                added++;
            }

            return added;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ollama query generation failed for research run; deterministic queries remain in use");
            return 0;
        }
    }

    private async Task<int> GenerateAdaptivePlanForRunAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var catalog = AdaptiveResearchPlanner.BuildDefaultSourceCatalog(
                webSearchAvailable: _options.EnableSearchProvider,
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
                maxActions: 8,
                isFollowUp: false,
                remainingOllamaCalls: Math.Max(0, _options.MaxOllamaCallsPerRun - state.OllamaCallsUsed),
                remainingSearchRequests: Math.Max(0, _options.MaxSearchRequests),
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

            await RecordLifecycleEventAsync(
                context.ResearchRunId,
                ResearchRunEventType.AdaptivePlanProposed,
                ResearchRunEventSeverity.Info,
                state.LastAdaptiveStrategySummary ?? $"Adaptive research plan proposed {added} actions.",
                new
                {
                    actionsAdded = added,
                    adaptiveAccepted = state.AdaptiveActionsAccepted,
                    adaptiveRejected = state.AdaptiveActionsRejected,
                    abandonedPatterns = state.AbandonedSearchPatterns.Take(10).ToList(),
                    decisions = state.IntelligenceDecisions.TakeLast(8).Select(x => new
                    {
                        x.ActionType,
                        x.Query,
                        x.Reason,
                        x.AddressesRequirements
                    }).ToList()
                },
                cancellationToken,
                ResearchPipelineStage.Discovery);
            return added;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Adaptive Ollama research plan failed; deterministic queries remain in use");
            return 0;
        }
    }

    private async Task RecordLifecycleEventAsync(
        Guid researchRunId,
        ResearchRunEventType eventType,
        ResearchRunEventSeverity severity,
        string? message,
        object? data,
        CancellationToken cancellationToken,
        ResearchPipelineStage stage = ResearchPipelineStage.RunLifecycle,
        string? domain = null,
        string? url = null)
    {
        await _events.RecordAsync(
            new ResearchEventRecordRequest
            {
                ResearchRunId = researchRunId,
                EventType = eventType,
                Severity = severity,
                Stage = stage,
                Message = message,
                Data = data,
                Domain = domain,
                Url = url
            },
            cancellationToken);
    }

    protected async Task ProcessCandidateAsync(
        OutreachProject project,
        ResearchRun run,
        ProspectCandidate candidate,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate.CompanyName))
        {
            run.RejectedCount++;
            state.RecordRejection("Missing company name");
            return;
        }

        if (ProspectJunkClassifier.IsJunkOrganization(
                candidate.CompanyName,
                candidate.SourceUrl ?? candidate.Website,
                candidate.CompanyDescription))
        {
            run.RejectedCount++;
            state.IrrelevantEntitiesRejected++;
            state.RecordRejection("Navigation or legal copy");
            await RecordLifecycleEventAsync(
                run.Id,
                ResearchRunEventType.OrganizationCandidateRejected,
                ResearchRunEventSeverity.Info,
                $"Rejected non-organization text: {candidate.CompanyName}",
                new { company = candidate.CompanyName, source = candidate.SourceUrl },
                cancellationToken,
                ResearchPipelineStage.CompanyExtraction,
                candidate.Website,
                candidate.SourceUrl);
            return;
        }

        if (string.IsNullOrWhiteSpace(candidate.SourceUrl) && string.IsNullOrWhiteSpace(candidate.SourceName))
        {
            run.RejectedCount++;
            state.RecordRejection("Insufficient evidence");
            _logger.LogInformation("Prospect rejected missing source for {Company}", candidate.CompanyName);
            return;
        }

        var email = NormalizeEmailOrNull(candidate.Email);
        var phone = ProspectNormalizer.NormalizePhone(candidate.Phone);
        var whatsApp = ProspectNormalizer.NormalizeWhatsApp(candidate.WhatsApp) ?? phone;
        state.CandidatesValidated++;

        var match = await _duplicates.FindMatchAsync(
            new ProspectDuplicateLookupRequest
            {
                ProjectId = project.Id,
                CompanyName = candidate.CompanyName,
                Website = candidate.Website,
                City = candidate.City,
                Email = email,
                Phone = candidate.Phone,
                WhatsApp = candidate.WhatsApp,
                SourceUrl = candidate.SourceUrl,
                ExcludeProspectId = null,
                TrackExistingForMerge = true
            },
            cancellationToken);

        if (match.Kind != ProspectDuplicateKind.None)
        {
            var existingJunk = match.ExistingProspect is not null
                && ProspectJunkClassifier.IsJunkOrganization(
                    match.ExistingProspect.CompanyName,
                    match.ExistingProspect.SourceUrl ?? match.ExistingProspect.Website,
                    match.ExistingProspect.CompanyDescription);
            if (existingJunk)
            {
                state.ProspectsSkippedAsDuplicates++;
                state.PreviouslyKnownProspects++;
                await RecordLifecycleEventAsync(
                    run.Id,
                    ResearchRunEventType.OrganizationDeduplicated,
                    ResearchRunEventSeverity.Info,
                    $"Ignored previously stored junk organization: {candidate.CompanyName}",
                    new { company = candidate.CompanyName, kind = match.Kind.ToString() },
                    cancellationToken,
                    ResearchPipelineStage.ProspectMerge,
                    candidate.Website,
                    candidate.SourceUrl);
                return;
            }

            run.DuplicatesFound++;
            _logger.LogInformation("Duplicate detected {Kind} for {Company}", match.Kind, candidate.CompanyName);
        }

        if (match.ShouldSkip
            || match.Kind == ProspectDuplicateKind.DoNotContact
            || (project.DuplicateHandling == DuplicateHandlingMode.Exclude
                && match.Kind is ProspectDuplicateKind.EmailContacted
                    or ProspectDuplicateKind.WhatsAppContacted
                    or ProspectDuplicateKind.CrossProject
                    or ProspectDuplicateKind.DoNotContact))
        {
            state.ProspectsSkippedAsDuplicates++;
            state.RecordRejection("Duplicate");
            await RecordLifecycleEventAsync(
                run.Id,
                ResearchRunEventType.DuplicateSkipped,
                ResearchRunEventSeverity.Info,
                $"Duplicate skipped: {candidate.CompanyName} ({match.Kind})",
                new { company = candidate.CompanyName, kind = match.Kind.ToString() },
                cancellationToken,
                ResearchPipelineStage.ProspectMerge,
                candidate.Website,
                candidate.SourceUrl);
            return;
        }

        if (project.DuplicateHandling == DuplicateHandlingMode.Exclude
            && match.ShouldMerge
            && match.Kind is not ProspectDuplicateKind.SameProject
                and not ProspectDuplicateKind.SameCompany
                and not ProspectDuplicateKind.SameWebsite
                and not ProspectDuplicateKind.SamePhone
                and not ProspectDuplicateKind.PossibleDuplicate)
        {
            state.ProspectsSkippedAsDuplicates++;
            state.RecordRejection("Duplicate");
            await RecordLifecycleEventAsync(
                run.Id,
                ResearchRunEventType.DuplicateSkipped,
                ResearchRunEventSeverity.Info,
                $"Duplicate skipped: {candidate.CompanyName} ({match.Kind})",
                new { company = candidate.CompanyName, kind = match.Kind.ToString() },
                cancellationToken,
                ResearchPipelineStage.ProspectMerge,
                candidate.Website,
                candidate.SourceUrl);
            return;
        }

        await RecordLifecycleEventAsync(
            run.Id,
            ResearchRunEventType.CompanyExtracted,
            ResearchRunEventSeverity.Info,
            $"Company candidate: {candidate.CompanyName}",
            new { company = candidate.CompanyName, source = candidate.SourceName },
            cancellationToken,
            ResearchPipelineStage.CompanyExtraction,
            candidate.Website,
            candidate.SourceUrl);

        var classification = new AiClassificationRequest
        {
            ProjectName = project.Name,
            ProjectDescription = project.Description,
            ResearchInstructions = project.ResearchInstructions,
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Countries = project.Countries.Select(x => x.Name).ToList(),
            Industries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            TargetRoles = project.TargetRoles.Select(x => x.Role).ToList(),
            CompanyName = candidate.CompanyName.Trim(),
            Website = TrimOrNull(candidate.Website),
            Country = TrimOrNull(candidate.Country),
            City = TrimOrNull(candidate.City),
            Industry = TrimOrNull(candidate.Industry),
            CompanyDescription = TrimOrNull(candidate.CompanyDescription),
            CompanySize = TrimOrNull(candidate.CompanySize),
            ContactName = TrimOrNull(candidate.ContactName),
            ContactRole = TrimOrNull(candidate.ContactRole),
            Email = email,
            WhatsApp = whatsApp,
            SourceUrl = TrimOrNull(candidate.SourceUrl),
            ResearchNotes = TrimOrNull(candidate.ResearchNotes)
        };

        var queryVersion = run.ResearchQueryVersionId is null
            ? null
            : await _db.ResearchQueryVersions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == run.ResearchQueryVersionId.Value, cancellationToken);

        var evidenceItems = await LoadEvidenceForCandidateAsync(run.Id, candidate, cancellationToken);
        var ruleEvaluation = _qualificationService.Evaluate(new QualificationEvaluationRequest
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            Candidate = candidate,
            QueryVersion = queryVersion,
            RequireEmail = run.IncludeEmail,
            MinQualificationScore = run.MinQualificationScore,
            Keywords = classification.Keywords,
            ExcludedKeywords = classification.ExcludedKeywords,
            Countries = classification.Countries,
            Industries = classification.Industries,
            ExcludedIndustries = classification.ExcludedIndustries,
            EvidenceItems = evidenceItems
        });

        AiQualificationResult qualification;
        try
        {
            qualification = await _ai.QualifyAsync(classification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI qualification failed for {Company}", candidate.CompanyName);
            qualification = new AiQualificationResult
            {
                IsRelevant = true,
                NeedsReview = true,
                IsQualified = false,
                QualificationScore = 50,
                Summary = "Needs review because AI qualification was unavailable.",
                Industry = candidate.Industry,
                CompanySize = candidate.CompanySize,
                ContactRole = candidate.ContactRole,
                EmailConfidenceScore = email is null ? null : 50,
                WhatsAppConfidenceScore = whatsApp is null ? null : 50
            };
        }

        ApplyRuleEvaluation(qualification, ruleEvaluation);
        ApplyHardConstraintConflictGuard(project, state, qualification, candidate);
        AppendCountryEvidenceNotes(candidate, ruleEvaluation);
        if (!HasMinimumDiscoverableEvidence(candidate, ruleEvaluation))
        {
            qualification.IsRelevant = false;
            qualification.IsQualified = false;
            qualification.NeedsReview = false;
            qualification.QualificationScore = Math.Min(qualification.QualificationScore, 30);
            qualification.Summary = AppendNote(
                qualification.Summary,
                "Rejected empty discovery shell: missing official website or verified identity.");
        }

        if (ruleEvaluation.IsIdentityVerified)
        {
            state.IdentityVerifiedBusinesses++;
        }

        if (ruleEvaluation.IsContactable)
        {
            state.ContactableProspects++;
        }

        if (ruleEvaluation.IsEmailReady)
        {
            state.EmailReadyProspects++;
        }

        if (match.NeedsReview)
        {
            qualification.NeedsReview = true;
        }

        if (!qualification.IsRelevant || qualification.QualificationScore < Math.Min(run.MinQualificationScore, 40))
        {
            var preserveDiscovered = HasMinimumDiscoverableEvidence(candidate, ruleEvaluation);
            var rejectReason = ResolveRejectionReason(qualification, candidate, ruleEvaluation);
            if (preserveDiscovered && match.Kind == ProspectDuplicateKind.None)
            {
                state.NewlyDiscoveredProspects++;
                await RecordLifecycleEventAsync(
                    run.Id,
                    ResearchRunEventType.CompanyExtracted,
                    ResearchRunEventSeverity.Info,
                    $"Discovered (not campaign-qualified): {candidate.CompanyName}",
                    new
                    {
                        company = candidate.CompanyName,
                        score = qualification.QualificationScore,
                        reason = rejectReason,
                        missingCriteria = ruleEvaluation.MissingCriteria
                    },
                    cancellationToken,
                    ResearchPipelineStage.Qualification,
                    candidate.Website,
                    candidate.SourceUrl);
            }
            else
            {
                run.RejectedCount++;
                state.BusinessesRejected++;
                state.RecordRejection(rejectReason);
                if (rejectReason.Contains("entity", StringComparison.OrdinalIgnoreCase)
                    || rejectReason.Contains("identity", StringComparison.OrdinalIgnoreCase))
                {
                    state.IrrelevantEntitiesRejected++;
                }

                _logger.LogInformation("Prospect rejected {Company}", candidate.CompanyName);
                await RecordLifecycleEventAsync(
                    run.Id,
                    ResearchRunEventType.Rejected,
                    ResearchRunEventSeverity.Info,
                    $"Rejected: {candidate.CompanyName}",
                    new
                    {
                        company = candidate.CompanyName,
                        score = qualification.QualificationScore,
                        reason = rejectReason,
                        missingCriteria = ruleEvaluation.MissingCriteria,
                        mandatoryFailures = ruleEvaluation.Criteria
                            .Where(x => x.IsMandatory && !x.Passed)
                            .Select(x => x.CriterionKey)
                            .ToList()
                    },
                    cancellationToken,
                    ResearchPipelineStage.Qualification,
                    candidate.Website,
                    candidate.SourceUrl);
            }

            if (preserveDiscovered || match.ShouldMerge)
            {
                RecordAttributedContactYield(run, state, candidate, email, whatsApp, match);
                await PersistProspectAsync(
                    project,
                    run,
                    candidate,
                    email,
                    whatsApp,
                    qualification,
                    preserveDiscovered ? ProspectStatus.Discovered : ProspectStatus.Rejected,
                    match,
                    null,
                    state,
                    cancellationToken);
            }

            return;
        }

        var hasRequiredContact = HasAcceptableContact(run, email, whatsApp);
        var status = ResolveStatus(
            qualification,
            run.MinQualificationScore,
            match,
            project.DuplicateHandling,
            hasRequiredContact);

        if (!hasRequiredContact
            && ruleEvaluation.IsIdentityVerified
            && status is ProspectStatus.Qualified or ProspectStatus.ReadyForOutreach or ProspectStatus.NeedsReview)
        {
            qualification.NeedsReview = true;
            status = ProspectStatus.NeedsReview;
            qualification.Summary = AppendNote(
                qualification.Summary,
                "Business qualified; contact method still missing (email or WhatsApp).");
        }
        else if (!hasRequiredContact
                 && !ruleEvaluation.IsIdentityVerified
                 && status is ProspectStatus.NeedsReview)
        {
            qualification.NeedsReview = false;
            qualification.IsQualified = false;
            qualification.IsRelevant = false;
            status = ProspectStatus.Rejected;
            qualification.Summary = AppendNote(
                qualification.Summary,
                "Rejected empty or unverified discovery shell instead of needs-review.");
        }

        if (status == ProspectStatus.Qualified || status == ProspectStatus.ReadyForOutreach)
        {
            if (!match.ShouldMerge)
            {
                run.QualifiedCount++;
            }

            _logger.LogInformation("Prospect qualified {Company}", candidate.CompanyName);
            await RecordLifecycleEventAsync(
                run.Id,
                ResearchRunEventType.Qualified,
                ResearchRunEventSeverity.Info,
                $"Qualified: {candidate.CompanyName}",
                new
                {
                    company = candidate.CompanyName,
                    score = qualification.QualificationScore,
                    status = status.ToString()
                },
                cancellationToken,
                ResearchPipelineStage.Qualification,
                candidate.Website,
                candidate.SourceUrl);
        }
        else if (status == ProspectStatus.Rejected)
        {
            run.RejectedCount++;
            var rejectReason = ResolveRejectionReason(qualification, candidate, ruleEvaluation);
            state.RecordRejection(rejectReason);
            await RecordLifecycleEventAsync(
                run.Id,
                ResearchRunEventType.Rejected,
                ResearchRunEventSeverity.Info,
                $"Rejected: {candidate.CompanyName}",
                new
                {
                    company = candidate.CompanyName,
                    score = qualification.QualificationScore,
                    reason = rejectReason,
                    missingCriteria = ruleEvaluation.MissingCriteria
                },
                cancellationToken,
                ResearchPipelineStage.Qualification,
                candidate.Website,
                candidate.SourceUrl);
        }

        if (match.Kind == ProspectDuplicateKind.None)
        {
            state.NewlyDiscoveredProspects++;
        }
        else
        {
            state.PreviouslyKnownProspects++;
            if (match.ExistingProspect?.Status == ProspectStatus.Contacted)
            {
                state.PreviouslyContactedProspects++;
            }
        }

        if (status is ProspectStatus.ReadyForOutreach or ProspectStatus.Qualified
            && match.Kind == ProspectDuplicateKind.None)
        {
            state.NewlyOutreachEligibleProspects++;
        }

        RecordAttributedContactYield(run, state, candidate, email, whatsApp, match);

        if (!string.IsNullOrWhiteSpace(whatsApp) && !match.ShouldMerge)
        {
            run.ValidWhatsApp++;
        }

        if (status == ProspectStatus.Rejected && !match.ShouldMerge)
        {
            return;
        }

        var recommendation = await RecommendTemplateSafeAsync(project, classification, qualification, cancellationToken);
        var prospectId = await PersistProspectAsync(
            project,
            run,
            candidate,
            email,
            whatsApp,
            qualification,
            status,
            match,
            recommendation,
            state,
            cancellationToken);

        if (prospectId is not null)
        {
            await PersistQualificationCriteriaAsync(prospectId.Value, ruleEvaluation, cancellationToken);
            await AttachEvidenceToProspectAsync(prospectId.Value, evidenceItems, cancellationToken);
            await PersistAttributedContactEvidenceAsync(
                project.Id,
                run.Id,
                prospectId.Value,
                candidate,
                cancellationToken);
            await _discoverySourceRegistry.RecordQualificationYieldAsync(
                candidate.SourceUrl ?? candidate.Website,
                qualification.IsQualified,
                cancellationToken);
        }
    }

    private static void RecordAttributedContactYield(
        ResearchRun run,
        ResearchDiscoveryState state,
        ProspectCandidate candidate,
        string? email,
        string? whatsApp,
        ProspectDuplicateMatch match)
    {
        var attributedContactCount = CountAttributedContacts(candidate, email, whatsApp);
        if (match.ShouldMerge || attributedContactCount <= 0)
        {
            return;
        }

        run.ValidEmails += candidate.AttributedEmails.Count > 0
            ? candidate.AttributedEmails.Count
            : string.IsNullOrWhiteSpace(email) ? 0 : 1 + candidate.AdditionalEmails.Count;
        state.AttributedEmailsAccepted += Math.Max(
            candidate.AttributedEmails.Count,
            string.IsNullOrWhiteSpace(email) ? 0 : 1);
        state.PublicContactsExtracted += attributedContactCount;
        run.ContactsFound += attributedContactCount;
    }

    private static int CountAttributedContacts(
        ProspectCandidate candidate,
        string? email,
        string? whatsApp)
    {
        var count = candidate.AttributedEmails.Count + candidate.AttributedPhones.Count;
        if (count > 0)
        {
            return count;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            count++;
        }

        count += candidate.AdditionalEmails.Count;
        if (!string.IsNullOrWhiteSpace(candidate.Phone))
        {
            count++;
        }

        count += candidate.AdditionalPhones.Count;
        if (!string.IsNullOrWhiteSpace(whatsApp))
        {
            count++;
        }

        return count;
    }

    private async Task PersistAttributedContactEvidenceAsync(
        Guid projectId,
        Guid researchRunId,
        Guid prospectId,
        ProspectCandidate candidate,
        CancellationToken cancellationToken)
    {
        foreach (var contact in candidate.AttributedEmails.Concat(candidate.AttributedPhones))
        {
            if (string.IsNullOrWhiteSpace(contact.Value))
            {
                continue;
            }

            _db.ResearchEvidenceItems.Add(new ResearchEvidence
            {
                ProjectId = projectId,
                ResearchRunId = researchRunId,
                ProspectId = prospectId,
                FactType = contact.Channel,
                FactValue = contact.Value,
                ConfidenceKind = string.Equals(contact.VerificationStatus, "same_domain", StringComparison.OrdinalIgnoreCase)
                    ? EvidenceConfidenceKind.Verified
                    : EvidenceConfidenceKind.Unverified,
                SourceUrl = contact.SourceUrl ?? candidate.ContactEvidenceUrl ?? candidate.SourceUrl,
                Notes = $"{contact.Context}; {contact.AttributionReasonCode}; status={contact.VerificationStatus}",
                FirstDiscoveredAtUtc = contact.DiscoveredAtUtc,
                LastVerifiedAtUtc = DateTimeOffset.UtcNow
            });

            _db.InvestigationContacts.Add(new InvestigationContact
            {
                ResearchRunId = researchRunId,
                ProjectId = projectId,
                ProspectId = prospectId,
                OrganizationName = candidate.CompanyName,
                ExactValue = contact.Value,
                NormalizedValue = contact.NormalizedValue ?? contact.Value,
                ContactType = contact.Channel,
                SourceUrl = contact.SourceUrl ?? candidate.ContactEvidenceUrl ?? candidate.SourceUrl,
                PageTitle = contact.PageTitle,
                SurroundingText = contact.SurroundingText ?? contact.Context,
                AttributionEvidence = contact.AttributionReasonCode,
                ValidationStatus = contact.VerificationStatus,
                Confidence = contact.Confidence,
                ReviewStatus = contact.ReviewStatus,
                DiscoveredAtUtc = contact.DiscoveredAtUtc
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyHardConstraintConflictGuard(
        OutreachProject project,
        ResearchDiscoveryState state,
        AiQualificationResult qualification,
        ProspectCandidate candidate)
    {
        var spec = ProjectEligibilityPolicy.BuildFallbackSpec(new ProspectSourceContext
        {
            ProjectName = project.Name,
            Description = project.Description,
            ResearchInstructions = project.ResearchInstructions,
            Industries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Countries = project.Countries.Select(x => x.Name).ToList(),
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            IncludeEmail = project.EnableEmail
        });

        if (!ProjectEligibilityPolicy.AiClaimConflictsWithHardConstraints(
                spec,
                qualification.Industry ?? candidate.Industry,
                candidate.Country,
                qualification.IsRelevant))
        {
            return;
        }

        qualification.IsRelevant = false;
        qualification.IsQualified = false;
        qualification.NeedsReview = false;
        qualification.QualificationScore = Math.Min(qualification.QualificationScore, 20);
        qualification.Summary = AppendNote(
            qualification.Summary,
            "Rejected because AI claims conflicted with project hard constraints.");
        state.RecordRejection("AiHardConstraintConflict");
    }

    private static void ApplyRuleEvaluation(
        AiQualificationResult qualification,
        QualificationEvaluationResult rules)
    {
        var mandatoryFailed = rules.Criteria.Any(x => x.IsMandatory && !x.Passed);
        if (mandatoryFailed)
        {
            qualification.IsRelevant = false;
            qualification.IsQualified = false;
            qualification.NeedsReview = false;
            qualification.QualificationScore = rules.Score;
            qualification.Summary = rules.Summary;
            return;
        }

        var countryNeedsReview = rules.Criteria.Any(x =>
            x.CriterionKey == "targetCountry"
            && !x.Passed
            && !x.IsMandatory);
        if (countryNeedsReview)
        {
            qualification.IsQualified = false;
            qualification.IsRelevant = true;
            qualification.NeedsReview = true;
            qualification.QualificationScore = Math.Min(qualification.QualificationScore, rules.Score);
            qualification.Summary = AppendNote(rules.Summary, "Country evidence incomplete; outreach blocked.");
            return;
        }

        if (!qualification.IsRelevant)
        {
            qualification.IsQualified = false;
            qualification.NeedsReview = false;
            qualification.QualificationScore = Math.Min(qualification.QualificationScore, rules.Score);
            qualification.Summary = AppendNote(
                qualification.Summary,
                "LLM excluded this prospect; rule scoring cannot override that determination.");
            qualification.Summary = AppendNote(qualification.Summary, rules.Summary);
            return;
        }

        if (rules.IsQualified && qualification.IsQualified)
        {
            qualification.IsRelevant = true;
            qualification.QualificationScore = Math.Max(qualification.QualificationScore, rules.Score);
            if (!rules.IsIdentityVerified)
            {
                qualification.IsQualified = false;
                qualification.IsRelevant = false;
                qualification.NeedsReview = false;
                qualification.Summary = AppendNote(
                    qualification.Summary,
                    "Rejected: identity was not verified for an official business domain.");
                return;
            }
        }
        else if (rules.IsQualified && !qualification.IsQualified)
        {
            qualification.IsQualified = false;
            qualification.IsRelevant = true;
            qualification.NeedsReview = true;
            qualification.QualificationScore = Math.Min(qualification.QualificationScore, rules.Score);
            qualification.Summary = AppendNote(
                qualification.Summary,
                "Rule scoring passed but the LLM withheld qualification; marked for review.");
        }
        else if (rules.IsIdentityVerified)
        {
            qualification.IsQualified = false;
            qualification.IsRelevant = true;
            qualification.NeedsReview = true;
            qualification.QualificationScore = Math.Min(qualification.QualificationScore, rules.Score);
        }
        else
        {
            qualification.IsQualified = false;
            qualification.IsRelevant = false;
            qualification.NeedsReview = false;
            qualification.QualificationScore = Math.Min(qualification.QualificationScore, rules.Score);
        }

        qualification.Summary = AppendNote(qualification.Summary, rules.Summary);
    }

    private async Task<List<ResearchEvidence>> LoadEvidenceForCandidateAsync(
        Guid researchRunId,
        ProspectCandidate candidate,
        CancellationToken cancellationToken)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            urls.Add(url.Trim());
            var canonical = UrlCanonicalizer.Canonicalize(url);
            if (!string.IsNullOrWhiteSpace(canonical))
            {
                urls.Add(canonical);
            }
        }

        AddUrl(candidate.SourceUrl);
        AddUrl(candidate.Website);
        AddUrl(candidate.ContactEvidenceUrl);
        foreach (var extra in candidate.AdditionalSourceUrls)
        {
            AddUrl(extra);
        }

        foreach (var contact in candidate.AttributedEmails.Concat(candidate.AttributedPhones))
        {
            AddUrl(contact.SourceUrl);
        }

        if (urls.Count == 0)
        {
            return [];
        }

        var urlList = urls.ToList();
        var domain = UrlCanonicalizer.ExtractDomain(candidate.Website)
                     ?? UrlCanonicalizer.ExtractDomain(candidate.SourceUrl)
                     ?? candidate.OfficialDomain;
        var items = await _db.ResearchEvidenceItems
            .Where(x => x.ResearchRunId == researchRunId && x.SourceUrl != null)
            .ToListAsync(cancellationToken);

        return items
            .Where(x =>
            {
                if (urlList.Contains(x.SourceUrl!))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(domain))
                {
                    return false;
                }

                var evidenceDomain = UrlCanonicalizer.ExtractDomain(x.SourceUrl);
                return string.Equals(evidenceDomain, domain, StringComparison.OrdinalIgnoreCase);
            })
            .Take(80)
            .ToList();
    }

    private async Task PersistQualificationCriteriaAsync(
        Guid prospectId,
        QualificationEvaluationResult evaluation,
        CancellationToken cancellationToken)
    {
        foreach (var criterion in evaluation.Criteria)
        {
            criterion.ProspectId = prospectId;
            _db.QualificationCriterionResults.Add(criterion);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task AttachEvidenceToProspectAsync(
        Guid prospectId,
        List<ResearchEvidence> evidenceItems,
        CancellationToken cancellationToken)
    {
        foreach (var item in evidenceItems)
        {
            if (item.ProspectId is null)
            {
                item.ProspectId = prospectId;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static bool HasAcceptableContact(ResearchRun run, string? email, string? whatsApp)
    {
        if (!run.IncludeEmail && !run.IncludeWhatsApp)
        {
            return true;
        }

        if (run.IncludeEmail && !string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        if (run.IncludeWhatsApp && !string.IsNullOrWhiteSpace(whatsApp))
        {
            return true;
        }

        return false;
    }

    private static string ResolveRejectionReason(
        AiQualificationResult qualification,
        ProspectCandidate candidate,
        QualificationEvaluationResult? rules = null)
    {
        if (rules is not null)
        {
            var failedMandatory = rules.Criteria
                .Where(x => x.IsMandatory && !x.Passed)
                .Select(x => x.CriterionKey)
                .ToList();
            if (failedMandatory.Contains("entityType", StringComparer.OrdinalIgnoreCase))
            {
                return "Non-target entity type";
            }

            if (failedMandatory.Contains("companyIdentity", StringComparer.OrdinalIgnoreCase))
            {
                return "Weak company identity";
            }

            if (failedMandatory.Contains("targetCountry", StringComparer.OrdinalIgnoreCase))
            {
                return "Outside target country";
            }

            if (failedMandatory.Contains("targetIndustry", StringComparer.OrdinalIgnoreCase))
            {
                return "Industry mismatch";
            }

            if (failedMandatory.Contains("contactAttribution", StringComparer.OrdinalIgnoreCase))
            {
                return "Contact attribution failed";
            }

            if (failedMandatory.Contains("exclusionCriteria", StringComparer.OrdinalIgnoreCase))
            {
                return "Excluded by criteria";
            }

            if (failedMandatory.Contains("officialWebsite", StringComparer.OrdinalIgnoreCase))
            {
                return "Missing official website";
            }
        }

        var summary = qualification.Summary ?? string.Empty;
        if (summary.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            return "Business closed";
        }

        if (summary.Contains("supplier", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("equipment", StringComparison.OrdinalIgnoreCase))
        {
            return "Equipment supplier";
        }

        if (summary.Contains("personal trainer", StringComparison.OrdinalIgnoreCase))
        {
            return "Personal trainer only";
        }

        if (summary.Contains("outside", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("country", StringComparison.OrdinalIgnoreCase))
        {
            return "Outside target country";
        }

        if (summary.Contains("entity type", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("directory", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("listicle", StringComparison.OrdinalIgnoreCase))
        {
            return "Non-target entity type";
        }

        if (summary.Contains("industry", StringComparison.OrdinalIgnoreCase))
        {
            return "Industry mismatch";
        }

        if (!qualification.IsRelevant)
        {
            return "Low project fit";
        }

        if (string.IsNullOrWhiteSpace(candidate.Website) && string.IsNullOrWhiteSpace(candidate.SourceUrl))
        {
            return "Insufficient evidence";
        }

        return "Low project fit";
    }

    private static void AppendCountryEvidenceNotes(
        ProspectCandidate candidate,
        QualificationEvaluationResult rules)
    {
        var countryCriterion = rules.Criteria.FirstOrDefault(x => x.CriterionKey == "targetCountry");
        if (countryCriterion is null)
        {
            return;
        }

        var note =
            $"countryGate={candidate.CountryMatchReasonCode ?? (countryCriterion.Passed ? "country.matched" : "country.failed")}; "
            + $"country={candidate.Country ?? "unknown"}; "
            + $"evidence={candidate.CountryEvidenceSummary ?? "none"}; "
            + countryCriterion.Reason;
        candidate.ResearchNotes = AppendNote(candidate.ResearchNotes, note);
    }

    private static string AppendNote(string? existing, string note)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return note;
        }

        if (existing.Contains(note, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return $"{existing.Trim()} {note}";
    }

    private async Task<Guid?> PersistProspectAsync(
        OutreachProject project,
        ResearchRun run,
        ProspectCandidate candidate,
        string? email,
        string? whatsApp,
        AiQualificationResult qualification,
        ProspectStatus status,
        ProspectDuplicateMatch match,
        AiTemplateRecommendationResult? recommendation,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                var persistResult = await PersistProspectCoreAsync(
                    project,
                    run,
                    candidate,
                    email,
                    whatsApp,
                    qualification,
                    status,
                    match,
                    recommendation,
                    cancellationToken);
                if (persistResult.SavedNew)
                {
                    state.ProspectsSaved++;
                }

                return persistResult.ProspectId;
            }
            catch (DbUpdateException ex) when (attempt < 3 && IsUniqueConstraintViolation(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Concurrent prospect insert conflict for {Company}; retrying merge",
                    candidate.CompanyName);

                DetachAddedProspects();
                match = await _duplicates.FindMatchAsync(
                    new ProspectDuplicateLookupRequest
                    {
                        ProjectId = project.Id,
                        CompanyName = candidate.CompanyName,
                        Website = candidate.Website,
                        City = candidate.City,
                        Email = email,
                        Phone = candidate.Phone,
                        WhatsApp = candidate.WhatsApp,
                        SourceUrl = candidate.SourceUrl,
                        ExcludeProspectId = null,
                        TrackExistingForMerge = true
                    },
                    cancellationToken);

                if (match.ExistingProspect is null && !match.ShouldMerge)
                {
                    throw;
                }
            }
        }
    }

    private async Task<(bool SavedNew, Guid? ProspectId)> PersistProspectCoreAsync(
        OutreachProject project,
        ResearchRun run,
        ProspectCandidate candidate,
        string? email,
        string? whatsApp,
        AiQualificationResult qualification,
        ProspectStatus status,
        ProspectDuplicateMatch match,
        AiTemplateRecommendationResult? recommendation,
        CancellationToken cancellationToken)
    {
        var mergeInput = BuildMergeInput(candidate, email, whatsApp);
        var personalizationJson = await ExtractPersonalizationSafeAsync(candidate, email, cancellationToken);

        if (match.ShouldMerge && match.ExistingProspect is not null)
        {
            var existing = match.ExistingProspect;
            ProspectMergeHelper.MergeCandidateInto(existing, mergeInput);
            PreferVerifiedCompanyName(existing, candidate);
            ApplyAdditionalContacts(existing, candidate);
            ProspectMergeHelper.ApplyQualificationMerge(
                existing,
                TrimOrNull(qualification.Summary),
                qualification.QualificationScore,
                qualification.EmailConfidenceScore,
                qualification.WhatsAppConfidenceScore,
                status,
                status is ProspectStatus.Qualified or ProspectStatus.ReadyForOutreach,
                match.Kind,
                run.Id,
                recommendation?.TemplateId,
                recommendation?.Reason,
                recommendation?.Confidence);

            if (!string.IsNullOrWhiteSpace(personalizationJson) && string.IsNullOrWhiteSpace(existing.PersonalizationJson))
            {
                existing.PersonalizationJson = personalizationJson;
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Merged duplicate prospect {ProspectId} for {Company}",
                existing.Id,
                existing.CompanyName);
            return (false, existing.Id);
        }

        var prospect = new Prospect
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            CompanyName = candidate.CompanyName.Trim(),
            NormalizedCompanyName = ProspectNormalizer.NormalizeCompanyName(candidate.CompanyName),
            Website = TrimOrNull(candidate.Website),
            NormalizedWebsite = ProspectNormalizer.NormalizeWebsite(candidate.Website)
                ?? ProspectNormalizer.NormalizeWebsite(candidate.SourceUrl),
            Country = TrimOrNull(candidate.Country),
            City = TrimOrNull(candidate.City),
            NormalizedCity = ProspectNormalizer.NormalizeCity(candidate.City),
            Industry = TrimOrNull(qualification.Industry) ?? TrimOrNull(candidate.Industry),
            CompanyDescription = TrimOrNull(candidate.CompanyDescription),
            CompanySize = TrimOrNull(qualification.CompanySize) ?? TrimOrNull(candidate.CompanySize),
            ContactName = TrimOrNull(candidate.ContactName),
            ContactRole = TrimOrNull(qualification.ContactRole) ?? TrimOrNull(candidate.ContactRole),
            Email = email,
            NormalizedEmail = email is null ? null : EmailAddressParser.Normalize(email),
            Phone = TrimOrNull(candidate.Phone),
            NormalizedPhone = ProspectNormalizer.NormalizePhone(candidate.Phone),
            WhatsApp = TrimOrNull(candidate.WhatsApp),
            NormalizedWhatsApp = whatsApp,
            LinkedInUrl = TrimOrNull(candidate.LinkedInUrl),
            SourceUrl = TrimOrNull(candidate.SourceUrl),
            SourceUrlsJson = ProspectSourceUrlStore.Serialize(
                [candidate.SourceUrl, ..candidate.AdditionalSourceUrls]),
            SourceType = candidate.SourceType,
            SourceName = TrimOrNull(candidate.SourceName),
            ResearchNotes = TrimOrNull(candidate.ResearchNotes),
            QualificationSummary = TrimOrNull(qualification.Summary),
            QualificationScore = qualification.QualificationScore,
            EmailConfidenceScore = qualification.EmailConfidenceScore,
            WhatsAppConfidenceScore = qualification.WhatsAppConfidenceScore,
            IsQualified = status is ProspectStatus.Qualified or ProspectStatus.ReadyForOutreach
                && qualification.QualificationScore >= run.MinQualificationScore,
            Status = status,
            DuplicateKind = match.Kind,
            RecommendedTemplateId = recommendation?.TemplateId,
            RecommendedTemplateReason = recommendation?.Reason,
            RecommendedTemplateConfidence = recommendation?.Confidence,
            PersonalizationJson = personalizationJson
        };

        ApplyAdditionalContacts(prospect, candidate);
        _db.Prospects.Add(prospect);
        await _db.SaveChangesAsync(cancellationToken);
        return (true, prospect.Id);
    }

    private static void PreferVerifiedCompanyName(Prospect existing, ProspectCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.CompanyName))
        {
            return;
        }

        var incoming = candidate.CompanyName.Trim();
        var current = existing.CompanyName ?? string.Empty;
        if (CompanyIdentityResolver.IsGenericName(current)
            && !CompanyIdentityResolver.IsGenericName(incoming))
        {
            existing.CompanyName = incoming;
            existing.NormalizedCompanyName = ProspectNormalizer.NormalizeCompanyName(incoming);
            return;
        }

        if (!CompanyIdentityResolver.IsGenericName(incoming)
            && candidate.NameConfidence >= 70
            && (CompanyIdentityResolver.IsGenericName(current) || incoming.Length <= current.Length))
        {
            existing.CompanyName = incoming;
            existing.NormalizedCompanyName = ProspectNormalizer.NormalizeCompanyName(incoming);
        }
    }

    private static void ApplyAdditionalContacts(Prospect prospect, ProspectCandidate candidate)
    {
        if (candidate.AttributedEmails.Count > 0)
        {
            prospect.AdditionalEmailsJson = ProspectContactStore.MergeAttributedEmails(
                prospect.AdditionalEmailsJson,
                candidate.AttributedEmails);
        }

        if (candidate.AttributedPhones.Count > 0)
        {
            prospect.AdditionalPhonesJson = ProspectContactStore.MergeAttributedPhones(
                prospect.AdditionalPhonesJson,
                candidate.AttributedPhones);
        }

        foreach (var extraEmail in candidate.AdditionalEmails)
        {
            ProspectMergeHelper.MergeCandidateInto(
                prospect,
                new ProspectMergeInput
                {
                    CompanyName = prospect.CompanyName,
                    Email = extraEmail
                });
        }

        foreach (var extraPhone in candidate.AdditionalPhones)
        {
            ProspectMergeHelper.MergeCandidateInto(
                prospect,
                new ProspectMergeInput
                {
                    CompanyName = prospect.CompanyName,
                    Phone = extraPhone
                });
        }

        if (!string.IsNullOrWhiteSpace(candidate.Address)
            && (string.IsNullOrWhiteSpace(prospect.CompanyDescription)
                || !prospect.CompanyDescription.Contains(candidate.Address, StringComparison.OrdinalIgnoreCase)))
        {
            prospect.ResearchNotes = AppendNote(
                prospect.ResearchNotes,
                $"address={candidate.Address}");
        }
    }

    private async Task<string?> ExtractPersonalizationSafeAsync(
        ProspectCandidate candidate,
        string? email,
        CancellationToken cancellationToken)
    {
        try
        {
            var personalization = await _ai.ExtractPersonalizationAsync(
                new AiClassificationRequest
                {
                    CompanyName = candidate.CompanyName,
                    ContactName = candidate.ContactName,
                    ContactRole = candidate.ContactRole,
                    Industry = candidate.Industry,
                    City = candidate.City,
                    Country = candidate.Country,
                    Email = email
                },
                cancellationToken);
            return PersonalizationSerializer.Serialize(personalization.Values);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Personalization extract failed for {Company}", candidate.CompanyName);
            return null;
        }
    }

    private void DetachAddedProspects()
    {
        if (_db is not DbContext ef)
        {
            return;
        }

        foreach (var entry in ef.ChangeTracker.Entries<Prospect>().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
               || message.Contains("unique", StringComparison.OrdinalIgnoreCase)
               || message.Contains("constraint", StringComparison.OrdinalIgnoreCase);
    }

    private static ProspectMergeInput BuildMergeInput(ProspectCandidate candidate, string? email, string? whatsApp)
    {
        return new ProspectMergeInput
        {
            CompanyName = candidate.CompanyName.Trim(),
            Website = TrimOrNull(candidate.Website),
            Country = TrimOrNull(candidate.Country),
            City = TrimOrNull(candidate.City),
            Industry = TrimOrNull(candidate.Industry),
            CompanyDescription = TrimOrNull(candidate.CompanyDescription),
            CompanySize = TrimOrNull(candidate.CompanySize),
            ContactName = TrimOrNull(candidate.ContactName),
            ContactRole = TrimOrNull(candidate.ContactRole),
            Email = email,
            Phone = TrimOrNull(candidate.Phone),
            WhatsApp = TrimOrNull(candidate.WhatsApp) ?? whatsApp,
            LinkedInUrl = TrimOrNull(candidate.LinkedInUrl),
            SourceUrl = TrimOrNull(candidate.SourceUrl),
            AdditionalSourceUrls = [..candidate.AdditionalSourceUrls],
            SourceType = candidate.SourceType,
            SourceName = TrimOrNull(candidate.SourceName),
            ResearchNotes = TrimOrNull(candidate.ResearchNotes)
        };
    }

    private async Task<AiTemplateRecommendationResult?> RecommendTemplateSafeAsync(
        OutreachProject project,
        AiClassificationRequest classification,
        AiQualificationResult qualification,
        CancellationToken cancellationToken)
    {
        if (project.Templates.Count == 0)
        {
            return null;
        }

        try
        {
            return await _ai.RecommendTemplateAsync(
                new AiTemplateRecommendationRequest
                {
                    Industry = qualification.Industry ?? classification.Industry,
                    CompanySize = qualification.CompanySize ?? classification.CompanySize,
                    CompanyName = classification.CompanyName,
                    ContactRole = qualification.ContactRole ?? classification.ContactRole,
                    Country = classification.Country,
                    Templates = project.Templates.Select(x => new AiTemplateOption
                    {
                        TemplateId = x.TemplateId,
                        Name = x.Template?.Name ?? x.TemplateId.ToString(),
                        Category = x.Template?.Category,
                        MatchIndustry = x.MatchIndustry,
                        MatchCompanySize = x.MatchCompanySize,
                        MatchNotes = x.MatchNotes
                    }).ToList()
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Template recommendation failed");
            return null;
        }
    }

    private static ProspectStatus ResolveStatus(
        AiQualificationResult qualification,
        int minScore,
        ProspectDuplicateMatch match,
        DuplicateHandlingMode duplicateHandling,
        bool hasRequiredContact)
    {
        if (match.Kind == ProspectDuplicateKind.DoNotContact)
        {
            return ProspectStatus.DoNotContact;
        }

        if (!qualification.IsRelevant)
        {
            return ProspectStatus.Rejected;
        }

        if (match.NeedsReview
            || (match.Kind != ProspectDuplicateKind.None && duplicateHandling == DuplicateHandlingMode.Warn))
        {
            return ProspectStatus.NeedsReview;
        }

        if (qualification.NeedsReview)
        {
            return ProspectStatus.NeedsReview;
        }

        if (qualification.IsQualified && qualification.QualificationScore >= minScore)
        {
            return hasRequiredContact ? ProspectStatus.ReadyForOutreach : ProspectStatus.NeedsReview;
        }

        if (qualification.IsRelevant && qualification.IsQualified)
        {
            return ProspectStatus.NeedsReview;
        }

        if (qualification.IsRelevant)
        {
            return ProspectStatus.Discovered;
        }

        return ProspectStatus.Rejected;
    }

    private static bool HasMinimumDiscoverableEvidence(
        ProspectCandidate candidate,
        QualificationEvaluationResult rules)
    {
        if (CompanyIdentityResolver.IsGenericName(candidate.CompanyName)
            || EntityClassifier.IsTopicArticleName(candidate.CompanyName)
            || EntityClassifier.IsListicleName(candidate.CompanyName))
        {
            return false;
        }

        if (candidate.SourceType == ProspectSourceType.Directory
            && candidate.NameConfidence >= 55
            && !string.IsNullOrWhiteSpace(candidate.SourceUrl))
        {
            return true;
        }

        var website = ProspectNormalizer.NormalizeWebsite(candidate.Website);
        var websiteHost = UrlCanonicalizer.ExtractDomain(website)
                          ?? UrlCanonicalizer.ExtractDomain(candidate.Website);
        if (string.IsNullOrWhiteSpace(website)
            || SystemDirectoryCatalog.IsSeedOrDirectoryHost(websiteHost)
            || SystemDirectoryCatalog.IsSeedOrDirectoryHost(candidate.SourceUrl))
        {
            return false;
        }

        if (!rules.IsIdentityVerified)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidate.CompanyDescription)
            && candidate.NameConfidence < 70)
        {
            return false;
        }

        return true;
    }

    private ProspectSourceContext BuildSourceContext(
        OutreachProject project,
        ResearchRun run,
        ResearchDiscoveryState discoveryState)
    {
        var context = new ProspectSourceContext
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            ProjectName = project.Name,
            Description = project.Description,
            ResearchInstructions = project.ResearchInstructions,
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Countries = project.Countries.Select(x => x.Name).ToList(),
            Industries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            TargetRoles = project.TargetRoles.Select(x => x.Role).ToList(),
            IncludeEmail = run.IncludeEmail,
            IncludeWhatsApp = run.IncludeWhatsApp,
            RequestedCount = run.RequestedProspectCount,
            DiscoveryState = discoveryState
        };

        context.Locations = ResearchSearchQueryGenerator.ResolveLocations(context);
        return context;
    }

    private async Task InterpretProjectAsync(
        OutreachProject project,
        ResearchRun run,
        ProspectSourceContext context,
        CancellationToken cancellationToken)
    {
        ResearchQueryVersion? version = null;
        if (run.ResearchQueryVersionId is not null)
        {
            version = await _db.ResearchQueryVersions
                .FirstOrDefaultAsync(x => x.Id == run.ResearchQueryVersionId.Value, cancellationToken);
        }

        var persisted = ProjectUnderstandingEngine.TryRead(version);
        var spec = persisted
                   ?? ProjectUnderstandingEngine.Interpret(new ProjectUnderstandingRequest
                   {
                       ProjectName = project.Name,
                       Description = project.Description,
                       ResearchInstructions = project.ResearchInstructions,
                       ApprovedIndustries = ResolveApprovedIndustries(context, version),
                       ExcludedIndustries = context.ExcludedIndustries,
                       Keywords = context.Keywords,
                       ExcludedKeywords = context.ExcludedKeywords,
                       Countries = context.Countries,
                       TargetRoles = context.TargetRoles,
                       QueryTargetIndustry = version?.TargetIndustry,
                       QueryBusinessCategories = ProjectUnderstandingEngine.ReadJsonList(version?.BusinessCategoriesJson),
                       QueryTargetCountry = version?.TargetCountry,
                       QueryCities = ProjectUnderstandingEngine.ReadJsonList(version?.CitiesJson),
                       QueryExclusionCriteria = ProjectUnderstandingEngine.ReadJsonList(version?.ExclusionCriteriaJson),
                       QueryRequireOfficialWebsite = version?.RequireOfficialWebsite ?? true,
                       QueryRequirePublicBusinessEmail = version?.RequirePublicBusinessEmail ?? run.IncludeEmail,
                       MinQualificationScore = run.MinQualificationScore,
                       TargetProspectCount = run.RequestedProspectCount,
                       IncludeEmail = run.IncludeEmail,
                       IncludeWhatsApp = run.IncludeWhatsApp
                   });

        if (version is not null && (persisted is null || spec.CorrectionReasons.Count > 0))
        {
            ProjectUnderstandingEngine.ApplyToQueryVersion(version, spec);
            await _db.SaveChangesAsync(cancellationToken);
        }

        context.Specification = spec;
        context.QueryVersion = version;
        if (spec.ApprovedIndustries.Count > 0)
        {
            context.Industries = spec.ApprovedIndustries.ToList();
        }

        if (spec.Cities.Count > 0)
        {
            context.Locations = spec.Cities.ToList();
        }

        context.DiscoveryState.CurrentInvestigationActivity = "searching_for_sources";
        await RecordLifecycleEventAsync(
            run.Id,
            ResearchRunEventType.ProjectInterpreted,
            ResearchRunEventSeverity.Info,
            $"Interpreted project with {spec.ApprovedIndustries.Count} approved industries and {spec.ExcludedIndustries.Count} exclusions.",
            new
            {
                approvedIndustries = spec.ApprovedIndustries,
                excludedIndustries = spec.ExcludedIndustries,
                cities = spec.Cities,
                corrections = spec.CorrectionReasons,
                issues = spec.ValidationIssues.Select(x => x.Code).ToList()
            },
            cancellationToken,
            ResearchPipelineStage.ProjectInterpretation);

        var planned = ResearchSearchQueryGenerator.GeneratePlan(context, Math.Clamp(_options.MaxSearchQueries, 1, 200));
        context.DiscoveryState.PlannedSearchQueries = planned.ToList();
        await RecordLifecycleEventAsync(
            run.Id,
            ResearchRunEventType.SearchQueryGenerated,
            ResearchRunEventSeverity.Info,
            $"Planned {planned.Count} search queries across {planned.Select(x => x.Industry).Distinct(StringComparer.OrdinalIgnoreCase).Count()} industries.",
            new
            {
                queryCount = planned.Count,
                industries = planned.Select(x => x.Industry).Distinct().ToList(),
                intents = planned.Select(x => x.Intent).Distinct().ToList()
            },
            cancellationToken,
            ResearchPipelineStage.SearchPlanning);
    }

    private static List<string> ResolveApprovedIndustries(
        ProspectSourceContext context,
        ResearchQueryVersion? version)
    {
        if (context.Industries.Count > 0)
        {
            return context.Industries.ToList();
        }

        var fromVersion = ProjectUnderstandingEngine.ReadJsonList(version?.BusinessCategoriesJson);
        if (fromVersion.Count > 0)
        {
            return fromVersion;
        }

        if (!string.IsNullOrWhiteSpace(version?.TargetIndustry))
        {
            return [version.TargetIndustry.Trim()];
        }

        return [];
    }

    private static int CountDistinctCompanies(List<ProspectCandidate> candidates)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var name = ProspectNormalizer.NormalizeCompanyName(candidate.CompanyName);
            if (!string.IsNullOrWhiteSpace(name))
            {
                set.Add(name);
            }
        }

        return set.Count;
    }

    private static string? NormalizeEmailOrNull(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var parsed = EmailAddressParser.Parse(email);
        foreach (var valid in parsed.ValidEmails)
        {
            if (EmailAddressParser.IsAcceptableBusinessEmail(valid))
            {
                return valid;
            }
        }

        return null;
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsTimeoutCancellation(Exception ex)
    {
        if (ex is TaskCanceledException or TimeoutException or OperationCanceledException)
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
               || message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
               || message.Contains("cancelled", StringComparison.OrdinalIgnoreCase);
    }
}
