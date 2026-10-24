using System.Text.Json;
using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public class PendingUrlDiagnostic
{
    public string Url { get; set; } = string.Empty;

    public int Priority { get; set; }

    public string PagePurpose { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

public class ResearchDiscoveryState
{
    public HashSet<string> SeenHosts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> SeenCandidateKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> CompletedSourceNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool OllamaChecked { get; set; }

    public bool OllamaReachable { get; set; }

    public bool OllamaModelAvailable { get; set; }

    public int OllamaQueriesGenerated { get; set; }

    public int OllamaCallsUsed { get; set; }

    public int AdaptiveActionsAccepted { get; set; }

    public int AdaptiveActionsRejected { get; set; }

    public string? LastAdaptiveStrategySummary { get; set; }

    public List<string> AbandonedSearchPatterns { get; set; } = [];

    public List<ResearchIntelligenceDecisionRecord> IntelligenceDecisions { get; set; } = [];

    public string? OllamaModel { get; set; }

    public List<string> GeneratedQueries { get; set; } = [];

    public List<PlannedSearchQuery> PlannedSearchQueries { get; set; } = [];

    public HashSet<string> ExecutedSearchQueryKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> LowYieldQueryKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> QueriesExecutedByIndustry { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> RelevantHitsByIndustry { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? CurrentInvestigationActivity { get; set; }

    public int NextQueryIndex { get; set; }

    public int QueryExpansionRound { get; set; }

    public int SearchQueriesExecuted { get; set; }

    public int SearchResultsReceived { get; set; }

    public int SearchRequestsSuccessful { get; set; }

    public int SearchRequestsFailed { get; set; }

    public int Http401Count { get; set; }

    public int Http429Count { get; set; }

    public int Http5xxCount { get; set; }

    public int TimeoutCount { get; set; }

    public int CandidatesDiscovered { get; set; }

    public int CandidatesAfterDeduplication { get; set; }

    public int CandidatesValidated { get; set; }

    public int ProspectsSaved { get; set; }

    public int ProspectsSkippedAsDuplicates { get; set; }

    public Dictionary<string, int> RejectionReasons { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool LimitsExhausted { get; set; }

    public string? LimitsExhaustedReason { get; set; }

    public bool SearchLimitsExhausted { get; set; }

    public string? SearchLimitsExhaustedReason { get; set; }

    public SearchProviderHealthState SearchProviderHealth { get; set; } = SearchProviderHealthState.Healthy;

    public string? SearchProviderHealthSummary { get; set; }

    public bool SearchHealthChecked { get; set; }

    public int ConsecutiveProviderFailures { get; set; }

    public DateTimeOffset LastHeartbeatAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Dictionary<string, long> StageDurationsMs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<PendingUrlDiagnostic> DeferredHighScoreUrls { get; set; } = [];

    public string? ActiveProviderName { get; set; }

    public string? AutomaticDiscoverySummary { get; set; }

    public List<DiscoveryProviderStat> AutomaticDiscoveryProviderStats { get; set; } = [];

    public int AutomaticDiscoveryDuplicatesRemoved { get; set; }

    public int AutomaticDiscoveryRejectedUnsafe { get; set; }

    public int UrlsRejectedFiltered { get; set; }

    public int UrlsAdmitted { get; set; }

    public int UrlsReusedFromCache { get; set; }

    public int UrlsSkippedKnownProspect { get; set; }

    public Dictionary<string, int> UrlRejectionReasons { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> FetchFailureReasons { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> HostFailureCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> HostOrganizationNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> HostOrganizationConfidence { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, DomainInvestigationPlan> DomainInvestigationPlans { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, OrganizationInvestigationState> OrganizationInvestigations { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> HostFetchAllowances { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> StrongOrganizationHosts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int UsefulPendingUrls { get; set; }

    public int IdentityVerifiedBusinesses { get; set; }

    public int EmailReadyProspects { get; set; }

    public int ContactableProspects { get; set; }

    public int UsefulUrlsLeftPendingAtStop { get; set; }

    public int NewlyDiscoveredProspects { get; set; }

    public int PreviouslyKnownProspects { get; set; }

    public int PreviouslyContactedProspects { get; set; }

    public int NewlyOutreachEligibleProspects { get; set; }

    public int IrrelevantEntitiesRejected { get; set; }

    public int AttributedEmailsAccepted { get; set; }

    public int RecoveryPasses { get; set; }

    public string? LastRecoveryTrigger { get; set; }

    public string? LastRecoveryStrategy { get; set; }

    public bool LastRecoveryImprovedYield { get; set; }

    public List<AdaptiveRecoveryTelemetry> RecoveryTelemetry { get; set; } = [];

    public bool YieldRecoveryAttempted { get; set; }

    public string? DiscoveryPlanSummary { get; set; }

    public List<DiscoveryStageResult> DiscoveryStages { get; set; } = [];

    public int UniqueDomainsDiscovered { get; set; }

    public int RecoveryAttempts { get; set; }

    public List<string> RecoveryStrategies { get; set; } = [];

    public bool EnableContactPageHints { get; set; }

    public int SourcesReused { get; set; }

    public int SourcesAdded { get; set; }

    public int SourcesSkipped { get; set; }

    public int SourcesUnavailable { get; set; }

    public int UrlsAccessible { get; set; }

    public int UrlsParsed { get; set; }

    public int UrlsUnavailable { get; set; }

    public int UrlsRobotsBlocked { get; set; }

    public int PagesFetched { get; set; }

    public int PagesQueued { get; set; }

    public int PagesSkipped { get; set; }

    public int DiscoveryBudgetConsumed { get; set; }

    public int FetchBudgetConsumed { get; set; }

    public int FetchBudgetLimit { get; set; }

    public int RequestedFetchBudget { get; set; }

    public int ConfiguredBatchSize { get; set; }

    public int BatchesStarted { get; set; }

    public int BatchesCompleted { get; set; }

    public int CrawlBatchesSinceSearch { get; set; }

    public int SearchBatchesCompleted { get; set; }

    public int LinksExtracted { get; set; }

    public int LinksAdmitted { get; set; }

    public int LinksRejected { get; set; }

    public int EmailCandidatesRejected { get; set; }

    public int EffectiveMaxLinksPerPage { get; set; }

    public int EffectiveMaxLinksPerDomain { get; set; }

    public int EffectiveSearchReservePercent { get; set; }

    public int FetchesAttempted { get; set; }

    public int FetchesSucceeded { get; set; }

    public int FetchesFailed { get; set; }

    public int FetchesSkipped { get; set; }

    public int BudgetReserved { get; set; }

    public int BudgetRemaining { get; set; }

    public int EligiblePendingUrls { get; set; }

    public int DeferredUrls { get; set; }

    public bool MoreEligibleWorkRemained { get; set; }

    public List<PendingUrlDiagnostic> TopPendingUrls { get; set; } = [];

    public int PagesClassifiedRelevant { get; set; }

    public int CandidateEntitiesExtracted { get; set; }

    public int UniqueEntitiesAfterDedup { get; set; }

    public int BusinessesRequiringReview { get; set; }

    public int BusinessesRejected { get; set; }

    public int DiscoveryBudgetLimit { get; set; }

    public string? LastBudgetStopReason { get; set; }

    public bool IndependentDiscoverySeeded { get; set; }

    public int CompaniesIdentified { get; set; }

    public int OrganizationMentionsExtracted { get; set; }

    public int UniqueOrganizationCandidates { get; set; }

    public int PublicContactsExtracted { get; set; }

    public int LiveVerifiedCompanies { get; set; }

    public DiscoveryStopReason DiscoveryStopReason { get; set; }

    public List<SourceEligibilityDecision> SourceEligibilityDecisions { get; set; } = [];

    public int RelevantIndustrySourcesAdmitted { get; set; }

    public int SecondaryRegistrySourcesAdmitted { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public void RecordStageDuration(string stageName, long elapsedMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(stageName) || elapsedMilliseconds < 0)
        {
            return;
        }

        var key = stageName.Trim();
        if (StageDurationsMs.TryGetValue(key, out var existing))
        {
            StageDurationsMs[key] = existing + elapsedMilliseconds;
            return;
        }

        StageDurationsMs[key] = elapsedMilliseconds;
    }

    public void RecordRejection(string reason)
    {
        IncrementCounter(RejectionReasons, reason);
    }

    public void RecordUrlRejection(string reasonCode)
    {
        UrlsRejectedFiltered++;
        IncrementCounter(UrlRejectionReasons, reasonCode);
    }

    private static void IncrementCounter(Dictionary<string, int> counters, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var key = reason.Trim();
        if (counters.TryGetValue(key, out var count))
        {
            counters[key] = count + 1;
            return;
        }

        counters[key] = 1;
    }

    public string SerializeRejectionReasons()
    {
        return JsonSerializer.Serialize(RejectionReasons);
    }

    public string SerializeProviderDiagnostics()
    {
        return JsonSerializer.Serialize(new
        {
            provider = ActiveProviderName ?? "IndependentCrawler",
            ollamaChecked = OllamaChecked,
            ollamaReachable = OllamaReachable,
            ollamaModelAvailable = OllamaModelAvailable,
            ollamaModel = OllamaModel,
            ollamaQueriesGenerated = OllamaQueriesGenerated,
            ollamaCallsUsed = OllamaCallsUsed,
            adaptiveActionsAccepted = AdaptiveActionsAccepted,
            adaptiveActionsRejected = AdaptiveActionsRejected,
            lastAdaptiveStrategySummary = LastAdaptiveStrategySummary,
            abandonedSearchPatterns = AbandonedSearchPatterns.Take(30).ToList(),
            intelligenceDecisions = IntelligenceDecisions.TakeLast(40).Select(x => new
            {
                actionType = x.ActionType,
                query = x.Query,
                reason = x.Reason,
                addressesRequirements = x.AddressesRequirements,
                evidenceSummary = x.EvidenceSummary,
                outcome = x.Outcome,
                wasUseful = x.WasUseful,
                nextPlanChange = x.NextPlanChange,
                recordedAtUtc = x.RecordedAtUtc
            }).ToList(),
            qualityMetrics = new
            {
                qualifiedPer100SearchRequests = RatePer100(ProspectsSaved, SearchRequestsSuccessful + SearchRequestsFailed),
                verifiedPer100FetchedPages = RatePer100(IdentityVerifiedBusinesses, PagesFetched),
                relevantFetchedPagePercent = Percent(PagesClassifiedRelevant, PagesFetched),
                duplicateRate = Percent(ProspectsSkippedAsDuplicates, Math.Max(1, CandidatesDiscovered)),
                publicContactDiscoveryRate = Percent(ContactableProspects, Math.Max(1, Math.Max(CompaniesIdentified, IdentityVerifiedBusinesses))),
                lowYieldQueriesAvoided = LowYieldQueryKeys.Count,
                adaptiveActionsAccepted = AdaptiveActionsAccepted,
                adaptiveActionsRejected = AdaptiveActionsRejected,
                ollamaCallsUsed = OllamaCallsUsed
            },
            queriesExecuted = SearchQueriesExecuted,
            requestsSuccessful = SearchRequestsSuccessful,
            requestsFailed = SearchRequestsFailed,
            resultsReceived = SearchResultsReceived,
            http401 = Http401Count,
            http429 = Http429Count,
            http5xx = Http5xxCount,
            timeouts = TimeoutCount,
            automaticDiscoverySummary = AutomaticDiscoverySummary,
            automaticDiscoveryDuplicatesRemoved = AutomaticDiscoveryDuplicatesRemoved,
            automaticDiscoveryRejectedUnsafe = AutomaticDiscoveryRejectedUnsafe,
            urlsRejectedFiltered = UrlsRejectedFiltered,
            urlsAdmitted = UrlsAdmitted,
            urlsReusedFromCache = UrlsReusedFromCache,
            urlsSkippedKnownProspect = UrlsSkippedKnownProspect,
            urlRejectionReasons = UrlRejectionReasons,
            fetchFailureReasons = FetchFailureReasons,
            hostFailureCounts = HostFailureCounts,
            hostFetchAllowances = HostFetchAllowances,
            strongOrganizationHosts = StrongOrganizationHosts.OrderBy(x => x).ToList(),
            usefulPendingUrls = UsefulPendingUrls,
            identityVerifiedBusinesses = IdentityVerifiedBusinesses,
            emailReadyProspects = EmailReadyProspects,
            contactableProspects = ContactableProspects,
            usefulUrlsLeftPendingAtStop = UsefulUrlsLeftPendingAtStop,
            newlyDiscoveredProspects = NewlyDiscoveredProspects,
            previouslyKnownProspects = PreviouslyKnownProspects,
            previouslyContactedProspects = PreviouslyContactedProspects,
            newlyOutreachEligibleProspects = NewlyOutreachEligibleProspects,
            irrelevantEntitiesRejected = IrrelevantEntitiesRejected,
            attributedEmailsAccepted = AttributedEmailsAccepted,
            discoveryPlanSummary = DiscoveryPlanSummary,
            uniqueDomainsDiscovered = UniqueDomainsDiscovered,
            recoveryAttempts = RecoveryAttempts > 0
                ? RecoveryAttempts
                : DiscoveryStages.Count(x => x.RecoveryTriggered),
            recoveryPasses = RecoveryPasses,
            recoveryStrategies = RecoveryStrategies,
            lastRecoveryTrigger = LastRecoveryTrigger,
            lastRecoveryStrategy = LastRecoveryStrategy,
            lastRecoveryImprovedYield = LastRecoveryImprovedYield,
            recoveryTelemetry = RecoveryTelemetry.Select(x => new
            {
                stageNumber = x.StageNumber,
                triggerSignal = x.TriggerSignal,
                strategy = x.Strategy,
                reason = x.Reason,
                candidatesBefore = x.CandidatesBefore,
                candidatesAfter = x.CandidatesAfter,
                improvedYield = x.ImprovedYield
            }).ToList(),
            discoveryStopReason = DiscoveryStopReason.ToString(),
            sourcesReused = SourcesReused,
            sourcesAdded = SourcesAdded,
            sourcesSkipped = SourcesSkipped,
            sourcesUnavailable = SourcesUnavailable,
            urlsAccessible = UrlsAccessible,
            urlsParsed = UrlsParsed,
            urlsUnavailable = UrlsUnavailable,
            urlsRobotsBlocked = UrlsRobotsBlocked,
            pagesFetched = PagesFetched,
            pagesQueued = PagesQueued,
            pagesSkipped = PagesSkipped,
            discoveryBudgetConsumed = DiscoveryBudgetConsumed,
            discoveryBudgetLimit = DiscoveryBudgetLimit,
            fetchBudgetConsumed = FetchBudgetConsumed,
            fetchBudgetLimit = FetchBudgetLimit,
            requestedFetchBudget = RequestedFetchBudget > 0 ? RequestedFetchBudget : FetchBudgetLimit,
            configuredBatchSize = ConfiguredBatchSize,
            batchesStarted = BatchesStarted,
            batchesCompleted = BatchesCompleted,
            crawlBatchesSinceSearch = CrawlBatchesSinceSearch,
            searchBatchesCompleted = SearchBatchesCompleted,
            linksExtracted = LinksExtracted,
            linksAdmitted = LinksAdmitted,
            linksRejected = LinksRejected,
            emailCandidatesRejected = EmailCandidatesRejected,
            effectiveMaxLinksPerPage = EffectiveMaxLinksPerPage,
            effectiveMaxLinksPerDomain = EffectiveMaxLinksPerDomain,
            effectiveSearchReservePercent = EffectiveSearchReservePercent,
            fetchesAttempted = FetchesAttempted,
            fetchesSucceeded = FetchesSucceeded,
            fetchesFailed = FetchesFailed,
            fetchesSkipped = FetchesSkipped,
            budgetReserved = BudgetReserved,
            budgetRemaining = BudgetRemaining,
            eligiblePendingUrls = EligiblePendingUrls,
            deferredUrls = DeferredUrls,
            moreEligibleWorkRemained = MoreEligibleWorkRemained,
            searchLimitsExhausted = SearchLimitsExhausted,
            searchLimitsExhaustedReason = SearchLimitsExhaustedReason,
            limitsExhausted = LimitsExhausted,
            limitsExhaustedReason = LimitsExhaustedReason,
            stageDurationsMs = StageDurationsMs,
            topPendingUrls = TopPendingUrls.Select(x => new
            {
                url = x.Url,
                priority = x.Priority,
                pagePurpose = x.PagePurpose,
                host = x.Host,
                sourceType = x.SourceType,
                reason = x.Reason
            }).ToList(),
            deferredHighScoreUrls = DeferredHighScoreUrls.Select(x => new
            {
                url = x.Url,
                priority = x.Priority,
                pagePurpose = x.PagePurpose,
                host = x.Host,
                sourceType = x.SourceType,
                reason = x.Reason
            }).ToList(),
            pagesClassifiedRelevant = PagesClassifiedRelevant,
            candidateEntitiesExtracted = CandidateEntitiesExtracted,
            uniqueEntitiesAfterDedup = UniqueEntitiesAfterDedup,
            businessesRequiringReview = BusinessesRequiringReview,
            businessesRejected = BusinessesRejected,
            lastBudgetStopReason = LastBudgetStopReason,
            companiesIdentified = CompaniesIdentified,
            organizationMentionsExtracted = OrganizationMentionsExtracted,
            uniqueOrganizationCandidates = UniqueOrganizationCandidates > 0
                ? UniqueOrganizationCandidates
                : UniqueEntitiesAfterDedup,
            publicContactsExtracted = PublicContactsExtracted,
            liveVerifiedCompanies = LiveVerifiedCompanies,
            prospectsQualified = ProspectsSaved,
            relevantIndustrySourcesAdmitted = RelevantIndustrySourcesAdmitted,
            secondaryRegistrySourcesAdmitted = SecondaryRegistrySourcesAdmitted,
            sourceEligibilityDecisions = SourceEligibilityDecisions.Select(x => new
            {
                sourceName = x.SourceName,
                sourceUrl = x.SourceUrl,
                sourceKind = x.SourceKind,
                state = x.State.ToString(),
                reasonCode = x.ReasonCode,
                explanation = x.Explanation,
                countryMatched = x.CountryMatched,
                industryMatched = x.IndustryMatched,
                matchedIndustryEvidence = x.MatchedIndustryEvidence,
                countsAsRelevantIndustrySource = x.CountsAsRelevantIndustrySource,
                canRevealTargetOrganizations = x.CanRevealTargetOrganizations,
                expectedDiscoveryValue = x.ExpectedDiscoveryValue
            }).ToList(),
            discoveryStages = DiscoveryStages.Select(x => new
            {
                stageNumber = x.StageNumber,
                stageName = x.StageName,
                providersAttempted = x.ProvidersAttempted,
                candidatesAdded = x.CandidatesAdded,
                recoveryTriggered = x.RecoveryTriggered,
                notes = x.Notes
            }).ToList(),
            discoveryProviders = AutomaticDiscoveryProviderStats.Select(x => new
            {
                providerName = x.ProviderName,
                attempted = x.Attempted,
                succeeded = x.Succeeded,
                producedUsefulCandidates = x.ProducedUsefulCandidates,
                candidatesReturned = x.CandidatesReturned,
                stageNumber = x.StageNumber,
                error = x.Error,
                zeroYieldReason = x.ZeroYieldReason
            }).ToList(),
            currentInvestigationActivity = CurrentInvestigationActivity,
            underCoveredIndustries = ResolveUnderCoveredIndustries(),
            nextBestInvestigationStrategy = ResolveNextBestStrategy(),
            searchProviderHealth = SearchProviderHealth.ToString(),
            searchProviderHealthSummary = SearchProviderHealthSummary,
            lastHeartbeatAtUtc = LastHeartbeatAtUtc,
            consecutiveProviderFailures = ConsecutiveProviderFailures
        });
    }

    public List<string> ResolveUnderCoveredIndustries()
    {
        var planned = PlannedSearchQueries
            .Select(x => x.Industry)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (planned.Count == 0)
        {
            return [];
        }

        var underCovered = new List<string>();
        foreach (var industry in planned)
        {
            var executed = QueriesExecutedByIndustry.TryGetValue(industry, out var count) ? count : 0;
            var relevant = RelevantHitsByIndustry.TryGetValue(industry, out var hits) ? hits : 0;
            if (executed == 0 || relevant == 0)
            {
                underCovered.Add(industry);
            }
        }

        return underCovered;
    }

    public string ResolveNextBestStrategy()
    {
        if (!string.IsNullOrWhiteSpace(LastAdaptiveStrategySummary))
        {
            return LastAdaptiveStrategySummary;
        }

        if (UsefulPendingUrls > 0 || EligiblePendingUrls > 0)
        {
            return "Investigate remaining high-value queued pages, directories, and official websites.";
        }

        var underCovered = ResolveUnderCoveredIndustries();
        if (underCovered.Count > 0)
        {
            return "Search under-covered industries and cities instead of repeating low-yield queries.";
        }

        if (CompaniesIdentified > 0 && PublicContactsExtracted == 0)
        {
            return "Follow official organization websites to extract public contact details.";
        }

        if (!string.IsNullOrWhiteSpace(LastBudgetStopReason)
            && LastBudgetStopReason.Contains("blocked", StringComparison.OrdinalIgnoreCase))
        {
            return "Remaining investigation is blocked by access restrictions.";
        }

        return "Useful investigation work is exhausted or a configured limit was reached.";
    }

    private static double RatePer100(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0;
        }

        return Math.Round(numerator * 100.0 / denominator, 2);
    }

    private static double Percent(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0;
        }

        return Math.Round(numerator * 100.0 / denominator, 2);
    }
}
