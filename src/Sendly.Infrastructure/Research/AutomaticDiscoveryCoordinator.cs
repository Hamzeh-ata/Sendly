using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Sendly.Infrastructure.Research;

public class AutomaticDiscoveryCoordinator : IAutomaticDiscoveryCoordinator
{
    private readonly IEnumerable<IUrlDiscoverySource> _sources;
    private readonly ILogger<AutomaticDiscoveryCoordinator> _logger;

    public AutomaticDiscoveryCoordinator(
        IEnumerable<IUrlDiscoverySource> sources,
        ILogger<AutomaticDiscoveryCoordinator> logger)
    {
        _sources = sources;
        _logger = logger;
    }

    public async Task<AutomaticDiscoveryResult> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var result = new AutomaticDiscoveryResult
        {
            Plan = context.Plan
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<DiscoveredUrlCandidate>();
        var sources = _sources.ToList();
        var maxStages = Math.Clamp(context.MaxDiscoveryStages, 1, 5);

        for (var stage = 1; stage <= maxStages; stage++)
        {
            context.DiscoveryStage = stage;
            var stageResult = new DiscoveryStageResult
            {
                StageNumber = stage,
                StageName = ResolveStageName(stage)
            };

            var stageProviders = SelectProvidersForStage(sources, context, stage);
            var addedBefore = candidates.Count;

            foreach (var source in stageProviders)
            {
                var alreadyAttempted = result.ProviderStats.Any(x =>
                    x.ProviderName == source.Name && x.Attempted);
                if (alreadyAttempted && stage > 1 && IsPrimary(source.Name)
                    && !context.PreferUnexploredProviders
                    && stage < 3)
                {
                    continue;
                }

                if (context.PreferUnexploredProviders
                    && context.ExploredProviderNames.Contains(source.Name)
                    && alreadyAttempted
                    && stage < 3)
                {
                    result.SourcesSkipped++;
                    continue;
                }

                if (stage >= 3
                    && alreadyAttempted
                    && IsPrimary(source.Name)
                    && !IsSecondary(source.Name))
                {
                    result.SourcesSkipped++;
                    continue;
                }

                var beforeCount = candidates.Count;
                var stat = await RunProviderAsync(
                    source,
                    context,
                    seen,
                    candidates,
                    result,
                    stage,
                    cancellationToken);
                result.ProviderStats.Add(stat);
                if (stat.Attempted)
                {
                    stageResult.ProvidersAttempted.Add(source.Name);
                    context.ExploredProviderNames.Add(source.Name);
                    if (stat.Succeeded && stat.ProducedUsefulCandidates)
                    {
                        result.SourcesAdded++;
                    }
                    else if (stat.Succeeded && !stat.ProducedUsefulCandidates)
                    {
                        result.SourcesReused++;
                        if (string.IsNullOrWhiteSpace(stat.ZeroYieldReason))
                        {
                            stat.ZeroYieldReason =
                                "Provider ran successfully but returned zero usable candidates for the current scope.";
                        }
                    }
                    else if (!stat.Succeeded)
                    {
                        result.SourcesUnavailable++;
                    }
                }
                else
                {
                    result.SourcesSkipped++;
                    if (string.IsNullOrWhiteSpace(stat.ZeroYieldReason))
                    {
                        stat.ZeroYieldReason = "Provider disabled or ineligible for current configuration.";
                    }
                }
            }

            stageResult.CandidatesAdded = candidates.Count - addedBefore;
            if (stageResult.ProvidersAttempted.Count == 0)
            {
                stageResult.Notes ??= stage >= 3
                    ? "Recovery stage had no remaining eligible providers after primary/secondary passes."
                    : "No providers were eligible or selected for this stage.";
            }

            result.Stages.Add(stageResult);

            if (candidates.Count >= context.MaxUrls)
            {
                break;
            }

            var eligibility = BuildEligibilitySnapshot(context);
            var interim = new AutomaticDiscoveryResult
            {
                Candidates = candidates,
                ProviderStats = result.ProviderStats.ToList(),
                DuplicatesRemoved = result.DuplicatesRemoved,
                RejectedFiltered = result.RejectedFiltered,
                RejectedUnsafe = result.RejectedUnsafe,
                UrlRejectionReasons = new Dictionary<string, int>(result.UrlRejectionReasons, StringComparer.OrdinalIgnoreCase),
                SourceEligibilityDecisions = context.SourceEligibilityDecisions.ToList()
            };

            if (DiscoveryCoverageEvaluator.HasSufficientRelevantCoverage(interim, context, eligibility)
                && !IsPriorResearchDominating(candidates, context))
            {
                stageResult.Notes =
                    $"Relevant coverage met ({eligibility.RelevantIndustrySourceCount} industry source(s), "
                    + $"{DiscoveryCoverageEvaluator.CountRelevantCandidates(candidates, eligibility)} relevant URL(s)).";
                break;
            }

            if (stage == 1 && IsPriorResearchDominating(candidates, context) && stage < maxStages)
            {
                stageResult.Notes =
                    "Prior research dominated discovery allocation; continuing for fresh provider diversity.";
                context.PreferUnexploredProviders = true;
            }

            var duplicateRate = seen.Count == 0
                ? 0
                : (int)Math.Round(result.DuplicatesRemoved * 100.0 / Math.Max(seen.Count + result.DuplicatesRemoved, 1));
            var yieldSignals = BuildYieldSignals(interim, context, result);
            var shouldContinue = DiscoveryCoverageEvaluator.ShouldContinueForFallback(
                interim,
                context,
                eligibility,
                stage,
                maxStages);
            var underperforming = yieldSignals.IsUnderperforming(minFetchesBeforeJudge: 0)
                                  || candidates.Count < context.MinCandidatesBeforeRecovery
                                  || IsPriorResearchDominating(candidates, context)
                                  || shouldContinue;

            if (stage < maxStages && underperforming)
            {
                var action = AdaptiveDiscoveryRecovery.DecideFromOutcomes(
                    context,
                    interim,
                    yieldSignals,
                    duplicateRate,
                    failedFetchPercent: 0);
                if (action is not null)
                {
                    var before = candidates.Count;
                    stageResult.RecoveryTriggered = true;
                    stageResult.Notes = $"{action.Strategy}: {action.Reason}";
                    AdaptiveDiscoveryRecovery.Apply(context, action);
                    result.RecoveryActions.Add(action.Strategy);
                    result.RecoveryTelemetry.Add(new AdaptiveRecoveryTelemetry
                    {
                        StageNumber = stage,
                        TriggerSignal = action.TriggerSignal ?? "unknown",
                        Strategy = action.Strategy,
                        Reason = action.Reason,
                        CandidatesBefore = before,
                        CandidatesAfter = before,
                        ImprovedYield = false
                    });
                }
                else if (shouldContinue || candidates.Count < context.MinCandidatesBeforeRecovery)
                {
                    stageResult.RecoveryTriggered = true;
                    stageResult.Notes = eligibility.RelevantIndustrySourceCount == 0
                        ? "No relevant industry sources yet; advancing to alternative discovery strategies."
                        : "Insufficient relevant coverage; advancing to recovery stage.";
                    ApplySynonymExpansion(context);
                    context.PreferUnexploredProviders = true;
                }

                continue;
            }

            if (DiscoveryCoverageEvaluator.HasSufficientRelevantCoverage(interim, context, eligibility)
                && !IsPriorResearchDominating(candidates, context)
                && !yieldSignals.IsUnderperforming(minFetchesBeforeJudge: 0))
            {
                break;
            }
        }

        var diversified = DiscoveryAdmissionPolicy.ApplyPriorResearchDiversity(candidates, context);
        if (context.PreferFreshSources)
        {
            diversified = DiscoveryAdmissionPolicy.PreferFreshOrdering(diversified);
        }

        var prioritized = Prioritize(diversified, context).ToList();
        var domainLimited = ApplyDomainBudget(prioritized, context.MaxCandidateDomains, result);
        result.Candidates = domainLimited.Take(context.MaxUrls).ToList();
        result.UniqueDomains = CountUniqueDomains(result.Candidates);
        result.SourceEligibilityDecisions = context.SourceEligibilityDecisions.ToList();
        result.RelevantIndustrySourcesAdmitted = result.Candidates.Count(x => x.IsIndustryRelevant);
        result.SecondaryRegistrySourcesAdmitted = result.Candidates.Count(x => x.IsSecondaryRegistry);
        FinalizeRecoveryTelemetry(result);
        var finalEligibility = BuildEligibilitySnapshot(context);
        ApplySecondaryOnlyGuard(result);
        result.StopReason = DiscoveryCoverageEvaluator.ResolveEmptyOrIrrelevantStop(result, finalEligibility);
        if (result.StopReason == DiscoveryStopReason.None && result.Candidates.Count == 0)
        {
            result.StopReason = ClassifyEmpty(result);
        }

        result.Summary = BuildSummary(result);
        return result;
    }

    private static void ApplySecondaryOnlyGuard(AutomaticDiscoveryResult result)
    {
        if (result.Candidates.Count == 0)
        {
            return;
        }

        var hasActionableSeed = result.Candidates.Any(x =>
            x.IsIndustryRelevant
            || x.SourceType is DiscoverySourceType.SeedUrl
                or DiscoverySourceType.PriorResearch
                or DiscoverySourceType.WebArchive);

        if (hasActionableSeed)
        {
            return;
        }

        var allExplicitlySecondary = result.Candidates.All(x => x.IsSecondaryRegistry);
        var eligibilityConfirmedNoPrimary = result.SourceEligibilityDecisions.Count > 0
            && result.SourceEligibilityDecisions.All(x => !x.CountsAsRelevantIndustrySource)
            && result.Candidates.All(x =>
                x.SourceType is DiscoverySourceType.DirectoryCatalog
                    or DiscoverySourceType.IndustryAssociation);

        if (allExplicitlySecondary || eligibilityConfirmedNoPrimary)
        {
            result.Candidates = [];
            result.UniqueDomains = 0;
            result.RelevantIndustrySourcesAdmitted = 0;
            result.StopReason = DiscoveryStopReason.NoRelevantDiscoverySources;
        }
    }

    private static SourceEligibilityResolveResult BuildEligibilitySnapshot(UrlDiscoveryContext context)
    {
        if (context.LastSourceEligibility is not null
            && context.LastSourceEligibility.Decisions.Count > 0)
        {
            return context.LastSourceEligibility;
        }

        var snapshot = new SourceEligibilityResolveResult
        {
            Decisions = context.SourceEligibilityDecisions.ToList()
        };
        foreach (var decision in context.SourceEligibilityDecisions)
        {
            if (decision.State == SourceEligibilityState.Eligible)
            {
                snapshot.EligiblePrimary.Add(decision);
            }
            else if (decision.State == SourceEligibilityState.EligibleSecondary)
            {
                snapshot.EligibleSecondary.Add(decision);
            }
        }

        return snapshot;
    }

    private static DiscoveryYieldSignals BuildYieldSignals(
        AutomaticDiscoveryResult interim,
        UrlDiscoveryContext context,
        AutomaticDiscoveryResult aggregate)
    {
        var prior = interim.Candidates.Count(x => x.SourceType == DiscoverySourceType.PriorResearch);
        var fresh = interim.Candidates.Count(x => x.SourceType != DiscoverySourceType.PriorResearch);
        var directorySources = interim.Candidates.Count(x =>
            x.SourceType is DiscoverySourceType.DirectoryCatalog or DiscoverySourceType.IndustryAssociation);
        return new DiscoveryYieldSignals
        {
            RequestedProspects = Math.Max(context.MinCandidatesBeforeRecovery, 1),
            QualifiedProspects = fresh,
            UniqueTargetBusinesses = CountUniqueDomains(interim.Candidates.Where(x =>
                x.SourceType is not DiscoverySourceType.DirectoryCatalog
                    and not DiscoverySourceType.IndustryAssociation
                    and not DiscoverySourceType.PriorResearch)),
            UrlsDiscovered = interim.Candidates.Count,
            AssetRejections = aggregate.RejectedFiltered,
            PriorSourceUrls = prior,
            FreshSourceUrls = fresh,
            SourceFailures = interim.ProviderStats.Count(x => x.Attempted && !x.Succeeded),
            SourcesAttempted = interim.ProviderStats.Count(x => x.Attempted),
            Duplicates = interim.DuplicatesRemoved,
            CandidatesProcessed = interim.Candidates.Count + interim.DuplicatesRemoved,
            DirectoryEntities = directorySources,
            EntitiesClassified = interim.Candidates.Count,
            IrrelevantEntities = 0,
            FetchesAttempted = 0,
            HtmlFetchesSucceeded = 0
        };
    }

    private static void FinalizeRecoveryTelemetry(AutomaticDiscoveryResult result)
    {
        if (result.RecoveryTelemetry.Count == 0)
        {
            return;
        }

        var finalCount = result.Candidates.Count;
        foreach (var item in result.RecoveryTelemetry)
        {
            item.CandidatesAfter = finalCount;
            item.ImprovedYield = finalCount > item.CandidatesBefore
                                 || result.Stages.Count > 1;
        }
    }

    private static bool IsPriorResearchDominating(
        List<DiscoveredUrlCandidate> candidates,
        UrlDiscoveryContext context)
    {
        if (candidates.Count == 0)
        {
            return false;
        }

        var priorCount = candidates.Count(x => x.SourceType == DiscoverySourceType.PriorResearch);
        var share = priorCount * 100.0 / candidates.Count;
        return share >= Math.Clamp(context.MaxPriorResearchSharePercent, 0, 100);
    }

    private async Task<DiscoveryProviderStat> RunProviderAsync(
        IUrlDiscoverySource source,
        UrlDiscoveryContext context,
        HashSet<string> seen,
        List<DiscoveredUrlCandidate> candidates,
        AutomaticDiscoveryResult result,
        int stage,
        CancellationToken cancellationToken)
    {
        var stat = new DiscoveryProviderStat
        {
            ProviderName = source.Name,
            StageNumber = stage,
            Attempted = false,
            Succeeded = false
        };

        if (!source.IsEnabled(context))
        {
            stat.ZeroYieldReason = "Provider is disabled by research options or query configuration.";
            return stat;
        }

        stat.Attempted = true;
        try
        {
            var found = await source.DiscoverAsync(context, cancellationToken);
            var accepted = 0;
            foreach (var item in found)
            {
                var admission = UrlAdmissionPolicy.Evaluate(item.Url, countries: context.Countries, industries: context.Industries);
                if (!admission.Accepted || string.IsNullOrWhiteSpace(admission.NormalizedUrl))
                {
                    result.RejectedFiltered++;
                    RecordRejectionReason(result, admission.ReasonCode);
                    continue;
                }

                var canonical = admission.NormalizedUrl;
                if (!Uri.TryCreate(canonical, UriKind.Absolute, out var uri)
                    || await SsrfGuard.IsBlockedUriAsync(uri, cancellationToken))
                {
                    result.RejectedUnsafe++;
                    RecordRejectionReason(result, "url.ssrf_or_unsafe");
                    continue;
                }

                if (context.PreferOfficialCompanyDomains
                    && admission.PagePurpose == UrlPagePurpose.DirectoryListing
                    && item.SourceType is DiscoverySourceType.PriorResearch or DiscoverySourceType.LinkExpansion)
                {
                    result.RejectedFiltered++;
                    RecordRejectionReason(result, "url.directory_deprioritized");
                    continue;
                }

                if (!seen.Add(canonical))
                {
                    result.DuplicatesRemoved++;
                    continue;
                }

                var domain = UrlCanonicalizer.ExtractDomain(canonical);
                if (context.DeprioritizeDeadDomains
                    && domain is not null
                    && context.DeadDomains.Contains(domain))
                {
                    result.SourcesSkipped++;
                    continue;
                }

                item.Url = canonical;
                candidates.Add(item);
                accepted++;
            }

            stat.Succeeded = true;
            stat.CandidatesReturned = accepted;
            stat.ProducedUsefulCandidates = accepted > 0;
            if (accepted == 0)
            {
                stat.ZeroYieldReason = found.Count == 0
                    ? "Catalog/provider had no eligible entries for the requested country/industry scope."
                    : "All returned URLs were filtered as unsafe, duplicate, or non-admissible.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stat.Succeeded = false;
            stat.ProducedUsefulCandidates = false;
            stat.Error = TruncateError(ex.Message);
            stat.ZeroYieldReason = "Provider threw an exception during discovery.";
            _logger.LogWarning(ex, "Discovery provider {Provider} failed at stage {Stage}", source.Name, stage);
        }

        return stat;
    }

    private static IEnumerable<IUrlDiscoverySource> SelectProvidersForStage(
        List<IUrlDiscoverySource> sources,
        UrlDiscoveryContext context,
        int stage)
    {
        return stage switch
        {
            1 => sources.Where(x => IsPrimary(x.Name)),
            2 => sources.Where(x => IsPrimary(x.Name) || IsSecondary(x.Name)),
            _ => sources
        };
    }

    private static string TruncateError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Discovery provider failed.";
        }

        var trimmed = message.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    private static bool IsPrimary(string name)
        => name is "DirectoryCatalog" or "PriorResearchUrls" or "SeedUrls"
            or "IndustryAssociationCatalog" or "WebSearchDiscovery";

    private static bool IsSecondary(string name)
        => name is "CommonCrawlIndex";

    private static void ApplySynonymExpansion(UrlDiscoveryContext context)
    {
        if (context.IndustrySynonyms.Count == 0)
        {
            return;
        }

        var seen = new HashSet<string>(context.Industries, StringComparer.OrdinalIgnoreCase);
        foreach (var synonym in context.IndustrySynonyms)
        {
            if (seen.Add(synonym))
            {
                context.Industries.Add(synonym);
            }
        }
    }

    private static List<DiscoveredUrlCandidate> ApplyDomainBudget(
        List<DiscoveredUrlCandidate> candidates,
        int maxDomains,
        AutomaticDiscoveryResult result)
    {
        var selectedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accepted = new List<DiscoveredUrlCandidate>();
        foreach (var candidate in candidates)
        {
            var domain = UrlCanonicalizer.ExtractDomain(candidate.Url) ?? candidate.Url;
            if (!selectedDomains.Contains(domain))
            {
                if (selectedDomains.Count >= maxDomains)
                {
                    continue;
                }

                selectedDomains.Add(domain);
            }

            accepted.Add(candidate);
        }

        return accepted;
    }

    private static int CountUniqueDomains(IEnumerable<DiscoveredUrlCandidate> candidates)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var domain = UrlCanonicalizer.ExtractDomain(candidate.Url);
            if (!string.IsNullOrWhiteSpace(domain))
            {
                domains.Add(domain);
            }
        }

        return domains.Count;
    }

    private static DiscoveryStopReason ClassifyEmpty(AutomaticDiscoveryResult result)
    {
        var attempted = result.ProviderStats.Count(x => x.Attempted);
        var failed = result.ProviderStats.Count(x => x.Attempted && !x.Succeeded);
        if (attempted > 0 && failed == attempted)
        {
            return DiscoveryStopReason.ProvidersFailed;
        }

        if (result.RelevantIndustrySourcesAdmitted == 0
            && result.SourceEligibilityDecisions.Count > 0
            && result.SourceEligibilityDecisions.All(x =>
                x.State is SourceEligibilityState.Ineligible
                    or SourceEligibilityState.TemporarilyUnavailable
                    or SourceEligibilityState.UnknownNeedsEvaluation
                    or SourceEligibilityState.EligibleSecondary))
        {
            return DiscoveryStopReason.NoRelevantDiscoverySources;
        }

        return DiscoveryStopReason.NoCandidatesDiscovered;
    }

    private static string ResolveStageName(int stage)
    {
        return stage switch
        {
            1 => "Primary registry and prior research",
            2 => "Synonym expansion and secondary providers",
            _ => "Archive and recovery providers"
        };
    }

    private static IEnumerable<DiscoveredUrlCandidate> Prioritize(
        List<DiscoveredUrlCandidate> candidates,
        UrlDiscoveryContext context)
    {
        return candidates
            .OrderByDescending(x => Score(x, context))
            .ThenBy(x => x.Depth)
            .ThenBy(x => x.Url.Length);
    }

    private static int Score(DiscoveredUrlCandidate candidate, UrlDiscoveryContext context)
    {
        var score = candidate.SourceType switch
        {
            DiscoverySourceType.IndustryAssociation => 40,
            DiscoverySourceType.DirectoryCatalog => 35,
            DiscoverySourceType.PriorResearch => 30,
            DiscoverySourceType.SeedUrl => 25,
            DiscoverySourceType.WebSearch => 30,
            DiscoverySourceType.WebArchive => 15,
            DiscoverySourceType.Sitemap => 20,
            DiscoverySourceType.LinkExpansion => 10,
            _ => 5
        };

        var haystack = $"{candidate.Url} {candidate.SourceName}".ToLowerInvariant();
        foreach (var industry in context.Industries.Concat(context.IndustrySynonyms))
        {
            if (!string.IsNullOrWhiteSpace(industry)
                && haystack.Contains(industry.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                score += 10;
            }
        }

        foreach (var country in context.Countries)
        {
            if (!string.IsNullOrWhiteSpace(country)
                && haystack.Contains(country.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                score += 5;
            }
        }

        return score;
    }

    private static void RecordRejectionReason(AutomaticDiscoveryResult result, string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return;
        }

        if (result.UrlRejectionReasons.TryGetValue(reasonCode, out var count))
        {
            result.UrlRejectionReasons[reasonCode] = count + 1;
            return;
        }

        result.UrlRejectionReasons[reasonCode] = 1;
    }

    private static string BuildSummary(AutomaticDiscoveryResult result)
    {
        var attempted = result.ProviderStats.Count(x => x.Attempted);
        var succeeded = result.ProviderStats.Count(x => x.Succeeded);
        var failed = result.ProviderStats.Count(x => x.Attempted && !x.Succeeded);
        var zeroReturn = result.ProviderStats.Count(x => x.Attempted && x.Succeeded && x.CandidatesReturned == 0);
        var stages = result.Stages.Count;
        var recovery = result.Stages.Count(x => x.RecoveryTriggered);
        var prior = result.Candidates.Count(x => x.SourceType == DiscoverySourceType.PriorResearch);
        var fresh = result.Candidates.Count - prior;

        if (result.Candidates.Count == 0 && attempted == 0)
        {
            return "No automatic discovery providers were eligible for this query scope (check enabled provider configuration).";
        }

        if (result.Candidates.Count == 0)
        {
            return $"Automatic discovery found no candidates after {stages} stage(s). Providers attempted: {attempted}, succeeded: {succeeded}, failed: {failed}, zero-return: {zeroReturn}. Filtered: {result.RejectedFiltered}. Relevant industry sources: {result.RelevantIndustrySourcesAdmitted}. Stop reason: {result.StopReason}.";
        }

        return $"Automatic discovery collected {result.Candidates.Count} candidate URLs ({result.UniqueDomains} domains; fresh={fresh}, prior={prior}; relevant={result.RelevantIndustrySourcesAdmitted}, secondary={result.SecondaryRegistrySourcesAdmitted}) from {succeeded}/{attempted} providers across {stages} stage(s), recovery stages: {recovery} (duplicates removed: {result.DuplicatesRemoved}, rejected unsafe: {result.RejectedUnsafe}, filtered: {result.RejectedFiltered}, zero-return providers: {zeroReturn}).";
    }
}
