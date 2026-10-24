using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public static class DiscoveryCoverageEvaluator
{
    public static bool HasSufficientRelevantCoverage(
        AutomaticDiscoveryResult result,
        UrlDiscoveryContext context,
        SourceEligibilityResolveResult? eligibility)
    {
        var relevantSourceCount = eligibility?.RelevantIndustrySourceCount ?? 0;
        if (relevantSourceCount <= 0)
        {
            return false;
        }

        var relevantCandidates = CountRelevantCandidates(result.Candidates, eligibility);
        var coveredSources = CountCoveredRelevantSources(result.Candidates, eligibility);
        var requiredSources = Math.Min(
            relevantSourceCount,
            Math.Max(2, Math.Min(5, Math.Max(1, context.MinCandidatesBeforeRecovery / 2))));
        if (coveredSources < requiredSources)
        {
            return false;
        }

        var minRelevant = Math.Max(
            requiredSources,
            Math.Min(context.MinCandidatesBeforeRecovery, Math.Max(requiredSources * 2, 5)));
        if (relevantCandidates < minRelevant)
        {
            return false;
        }

        var providersAttempted = result.ProviderStats.Count(x => x.Attempted);
        if (providersAttempted == 0)
        {
            return false;
        }

        var unexplored = context.Plan?.EligibleProviderNames
                             .Count(name => !context.ExploredProviderNames.Contains(name))
                         ?? 0;
        if (unexplored > 0 && relevantCandidates < context.MinCandidatesBeforeRecovery)
        {
            return false;
        }

        return true;
    }

    public static bool ShouldContinueForFallback(
        AutomaticDiscoveryResult result,
        UrlDiscoveryContext context,
        SourceEligibilityResolveResult? eligibility,
        int stage,
        int maxStages)
    {
        if (HasSufficientRelevantCoverage(result, context, eligibility))
        {
            return false;
        }

        if (stage >= maxStages)
        {
            return false;
        }

        var unexplored = context.Plan?.EligibleProviderNames
                             .Count(name => !context.ExploredProviderNames.Contains(name))
                         ?? 0;
        if (unexplored > 0)
        {
            return true;
        }

        var relevantCandidates = CountRelevantCandidates(result.Candidates, eligibility);
        if (relevantCandidates == 0 && result.Candidates.Count > 0)
        {
            return true;
        }

        return result.Candidates.Count < context.MinCandidatesBeforeRecovery;
    }

    public static DiscoveryStopReason ResolveEmptyOrIrrelevantStop(
        AutomaticDiscoveryResult result,
        SourceEligibilityResolveResult? eligibility)
    {
        var relevantCandidates = CountRelevantCandidates(result.Candidates, eligibility);
        var relevantSources = eligibility?.RelevantIndustrySourceCount ?? 0;
        if (result.Candidates.Count == 0 && relevantSources == 0)
        {
            return DiscoveryStopReason.NoRelevantDiscoverySources;
        }

        if (result.Candidates.Count > 0 && relevantCandidates == 0 && relevantSources == 0)
        {
            return DiscoveryStopReason.NoRelevantDiscoverySources;
        }

        if (result.Candidates.Count == 0)
        {
            return DiscoveryStopReason.NoCandidatesDiscovered;
        }

        return DiscoveryStopReason.None;
    }

    public static int CountRelevantCandidates(
        IReadOnlyList<DiscoveredUrlCandidate> candidates,
        SourceEligibilityResolveResult? eligibility)
    {
        if (candidates.Count == 0)
        {
            return 0;
        }

        if (eligibility is null)
        {
            return candidates.Count(x => x.IsIndustryRelevant);
        }

        var relevantNames = new HashSet<string>(
            eligibility.EligiblePrimary.Select(x => x.SourceName),
            StringComparer.OrdinalIgnoreCase);
        var relevantUrls = new HashSet<string>(
            eligibility.EligiblePrimary
                .Where(x => !string.IsNullOrWhiteSpace(x.SourceUrl))
                .Select(x => x.SourceUrl!),
            StringComparer.OrdinalIgnoreCase);

        var count = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.IsIndustryRelevant)
            {
                count++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidate.SourceName)
                && relevantNames.Contains(candidate.SourceName))
            {
                count++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidate.Url)
                && relevantUrls.Contains(candidate.Url))
            {
                count++;
            }
        }

        return count;
    }

    public static int CountCoveredRelevantSources(
        IReadOnlyList<DiscoveredUrlCandidate> candidates,
        SourceEligibilityResolveResult? eligibility)
    {
        if (eligibility is null || eligibility.EligiblePrimary.Count == 0)
        {
            return candidates
                .Where(x => x.IsIndustryRelevant && !string.IsNullOrWhiteSpace(x.SourceName))
                .Select(x => x.SourceName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var primary in eligibility.EligiblePrimary)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(primary.SourceName)
                    && string.Equals(candidate.SourceName, primary.SourceName, StringComparison.OrdinalIgnoreCase))
                {
                    covered.Add(primary.SourceName);
                    break;
                }

                if (!string.IsNullOrWhiteSpace(primary.SourceUrl)
                    && !string.IsNullOrWhiteSpace(candidate.Url)
                    && (string.Equals(candidate.Url, primary.SourceUrl, StringComparison.OrdinalIgnoreCase)
                        || candidate.Url.Contains(primary.SourceUrl, StringComparison.OrdinalIgnoreCase)
                        || primary.SourceUrl.Contains(candidate.Url, StringComparison.OrdinalIgnoreCase)))
                {
                    covered.Add(primary.SourceName ?? primary.SourceUrl!);
                    break;
                }
            }
        }

        return covered.Count;
    }
}
