using Sendly.Application.Abstractions;

namespace Sendly.Application.Services;

public static class ResearchIntelligenceMetrics
{
    public static ResearchQualityMetrics Compute(ResearchDiscoveryState state)
    {
        var searchRequests = Math.Max(0, state.SearchRequestsSuccessful + state.SearchRequestsFailed);
        var pagesFetched = Math.Max(0, state.PagesFetched);
        var qualified = Math.Max(0, state.ProspectsSaved);
        var verified = Math.Max(0, state.IdentityVerifiedBusinesses);
        var relevantPages = Math.Max(0, state.PagesClassifiedRelevant);
        var rejected = Math.Max(0, state.BusinessesRejected + state.IrrelevantEntitiesRejected);
        var duplicates = Math.Max(0, state.ProspectsSkippedAsDuplicates);
        var discovered = Math.Max(1, state.CandidatesDiscovered);
        var contactable = Math.Max(0, state.ContactableProspects);
        var identified = Math.Max(1, Math.Max(state.CompaniesIdentified, state.IdentityVerifiedBusinesses));
        var sourceSuccess = CountSourceSuccesses(state);
        var sourceChallenges = CountSourceChallenges(state);

        return new ResearchQualityMetrics
        {
            QualifiedBusinessesPer100SearchRequests = RatePer100(qualified, searchRequests),
            VerifiedBusinessesPer100FetchedPages = RatePer100(verified, pagesFetched),
            RelevantFetchedPagePercent = Percent(relevantPages, pagesFetched),
            FalsePositiveRate = Percent(rejected, discovered),
            DuplicateRate = Percent(duplicates, discovered),
            PublicContactDiscoveryRate = Percent(contactable, identified),
            IndustryCoveragePercent = ComputeIndustryCoverage(state),
            CountryCoveragePercent = ComputeCountryCoverage(state),
            LowYieldQueriesAvoided = state.LowYieldQueryKeys.Count,
            SourceSuccessCount = sourceSuccess,
            SourceChallengeCount = sourceChallenges,
            AdaptiveActionsAccepted = state.AdaptiveActionsAccepted,
            AdaptiveActionsRejected = state.AdaptiveActionsRejected,
            OllamaCallsUsed = state.OllamaCallsUsed
        };
    }

    public static string BuildCompactOutcomeSummary(ResearchDiscoveryState state, ResearchSpecification spec)
    {
        var metrics = Compute(state);
        var parts = new List<string>
        {
            $"queriesExecuted={state.SearchQueriesExecuted}",
            $"searchRequests={state.SearchRequestsSuccessful + state.SearchRequestsFailed}",
            $"usefulHits={Sum(state.RelevantHitsByIndustry)}",
            $"irrelevant={state.IrrelevantEntitiesRejected}",
            $"businessesVerified={state.IdentityVerifiedBusinesses}",
            $"qualified={state.ProspectsSaved}",
            $"rejected={state.BusinessesRejected}",
            $"duplicates={state.ProspectsSkippedAsDuplicates}",
            $"providerFailures={state.SearchRequestsFailed}",
            $"challenges={metrics.SourceChallengeCount}",
            $"pendingUsefulUrls={state.UsefulPendingUrls}",
            $"underCoveredIndustries={string.Join('|', state.ResolveUnderCoveredIndustries().Take(8))}",
            $"underCoveredLocations={string.Join('|', ResolveUnderCoveredLocations(state, spec).Take(8))}",
            $"lowYieldPatterns={state.LowYieldQueryKeys.Count}",
            $"abandonedPatterns={string.Join('|', state.AbandonedSearchPatterns.Take(8))}",
            $"qualifiedPer100Requests={metrics.QualifiedBusinessesPer100SearchRequests:0.##}",
            $"relevantPagePercent={metrics.RelevantFetchedPagePercent:0.##}"
        };
        return string.Join("; ", parts);
    }

    public static string BuildBudgetSummary(ResearchDiscoveryState state, int remainingOllamaCalls, int remainingSearchRequests)
    {
        return $"remainingSearchRequests={remainingSearchRequests}; remainingOllamaCalls={remainingOllamaCalls}; remainingFetches={state.BudgetRemaining}; fetchLimit={state.FetchBudgetLimit}; durationMinutes={(int)(DateTimeOffset.UtcNow - state.StartedAtUtc).TotalMinutes}";
    }

    public static List<string> ResolveUnderCoveredLocations(ResearchDiscoveryState state, ResearchSpecification spec)
    {
        var targets = new List<string>();
        AppendDistinct(targets, spec.Cities);
        if (targets.Count == 0)
        {
            AppendDistinct(targets, spec.Regions);
        }

        if (targets.Count == 0)
        {
            return [];
        }

        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var planned in state.PlannedSearchQueries)
        {
            if (state.ExecutedSearchQueryKeys.Contains(SearchCampaignPlanner.BuildKey(planned))
                && !string.IsNullOrWhiteSpace(planned.Location))
            {
                covered.Add(planned.Location.Trim());
            }
        }

        var under = new List<string>();
        foreach (var location in targets)
        {
            if (!covered.Contains(location))
            {
                under.Add(location);
            }
        }

        return under;
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

    private static double ComputeIndustryCoverage(ResearchDiscoveryState state)
    {
        var planned = state.PlannedSearchQueries
            .Select(x => x.Industry)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (planned.Count == 0)
        {
            return 0;
        }

        var covered = 0;
        foreach (var industry in planned)
        {
            if (state.RelevantHitsByIndustry.TryGetValue(industry, out var hits) && hits > 0)
            {
                covered++;
            }
        }

        return Percent(covered, planned.Count);
    }

    private static double ComputeCountryCoverage(ResearchDiscoveryState state)
    {
        var plannedCountries = state.PlannedSearchQueries
            .Select(x => x.Country)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (plannedCountries.Count == 0)
        {
            return state.ProspectsSaved > 0 ? 100 : 0;
        }

        var executedCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var planned in state.PlannedSearchQueries)
        {
            if (!string.IsNullOrWhiteSpace(planned.Country)
                && state.ExecutedSearchQueryKeys.Contains(SearchCampaignPlanner.BuildKey(planned)))
            {
                executedCountries.Add(planned.Country);
            }
        }

        return Percent(executedCountries.Count, plannedCountries.Count);
    }

    private static int CountSourceSuccesses(ResearchDiscoveryState state)
    {
        var count = 0;
        foreach (var provider in state.AutomaticDiscoveryProviderStats)
        {
            if (provider.Succeeded || provider.ProducedUsefulCandidates)
            {
                count++;
            }
        }

        return count + state.SearchRequestsSuccessful;
    }

    private static int CountSourceChallenges(ResearchDiscoveryState state)
    {
        var count = state.Http401Count + state.Http429Count;
        foreach (var reason in state.FetchFailureReasons)
        {
            if (reason.Key.Contains("captcha", StringComparison.OrdinalIgnoreCase)
                || reason.Key.Contains("challenge", StringComparison.OrdinalIgnoreCase)
                || reason.Key.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            {
                count += reason.Value;
            }
        }

        return count;
    }

    private static int Sum(Dictionary<string, int> values)
    {
        var total = 0;
        foreach (var item in values)
        {
            total += item.Value;
        }

        return total;
    }

    private static void AppendDistinct(List<string> target, IEnumerable<string> values)
    {
        var seen = new HashSet<string>(target, StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value.Trim()))
            {
                continue;
            }

            target.Add(value.Trim());
        }
    }
}
