using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public static class ResearchRunContinuationPolicy
{
    public const string FetchBatchLimitReason = "fetch.batch_limit";
    public const string WebSearchSourceName = "WebSearch";
    public const string IndependentCrawlerSourceName = "IndependentCrawler";

    public static bool ShouldContinueResearch(
        ResearchRun run,
        ResearchDiscoveryState state,
        int maxResearchDurationMinutes)
    {
        if (run.QualifiedCount >= run.RequestedProspectCount)
        {
            return false;
        }

        if (IsDurationExceeded(state, maxResearchDurationMinutes))
        {
            return false;
        }

        if (state.LimitsExhausted)
        {
            if (IsSearchOnlyLimitReason(state.LimitsExhaustedReason)
                && HasEligibleIndependentCrawlWork(state))
            {
                return true;
            }

            if (IsCatalogGapReason(state.LimitsExhaustedReason)
                && !state.CompletedSourceNames.Contains(WebSearchSourceName))
            {
                return true;
            }

            return false;
        }

        return true;
    }

    public static bool ShouldContinueAfterEmptyProspectBatch(
        ResearchDiscoveryState state,
        int pagesFetchedBefore,
        int pendingBefore)
    {
        if (state.FetchBudgetLimit > 0 && state.PagesFetched >= state.FetchBudgetLimit)
        {
            return false;
        }

        if (!HasEligibleIndependentCrawlWork(state))
        {
            return false;
        }

        var madeFetchProgress = state.PagesFetched > pagesFetchedBefore;
        var queueShrunk = state.PagesQueued < pendingBefore;
        var batchLimited = IsBatchLimitStop(state.LastBudgetStopReason);

        return madeFetchProgress || batchLimited || queueShrunk;
    }

    public static bool ShouldDeferSearchForIndependentCrawl(ResearchDiscoveryState state)
    {
        if (state.CompletedSourceNames.Contains(IndependentCrawlerSourceName))
        {
            return false;
        }

        if (state.SearchLimitsExhausted
            || state.CompletedSourceNames.Contains(WebSearchSourceName))
        {
            return true;
        }

        if (!HasEligibleIndependentCrawlWork(state))
        {
            return false;
        }

        if (state.CrawlBatchesSinceSearch >= 1)
        {
            return false;
        }

        return IsBatchLimitStop(state.LastBudgetStopReason);
    }

    public static bool IsSearchDue(ResearchDiscoveryState state)
    {
        if (state.CompletedSourceNames.Contains(WebSearchSourceName)
            || state.SearchLimitsExhausted)
        {
            return false;
        }

        return state.CrawlBatchesSinceSearch >= 1;
    }

    public static bool ShouldRetrySearchProvider(ResearchDiscoveryState state)
    {
        if (state.ConsecutiveProviderFailures >= 2)
        {
            return false;
        }

        return state.SearchProviderHealth is not SearchProviderHealthState.Unavailable
            and not SearchProviderHealthState.InvalidResponse
            || state.ConsecutiveProviderFailures == 0;
    }

    public static int ResolveLinkExpansionFetchCap(
        int pagesFetched,
        int fetchBudgetLimit,
        int reservePercent,
        bool searchMayStillSupplyUrls)
    {
        if (fetchBudgetLimit <= 0)
        {
            return int.MaxValue;
        }

        if (!searchMayStillSupplyUrls)
        {
            return fetchBudgetLimit;
        }

        var reserve = (int)Math.Ceiling(fetchBudgetLimit * Math.Clamp(reservePercent, 0, 80) / 100.0);
        return Math.Max(0, fetchBudgetLimit - reserve);
    }

    public static bool HasEligibleIndependentCrawlWork(ResearchDiscoveryState state)
    {
        if (state.FetchBudgetLimit > 0 && state.PagesFetched >= state.FetchBudgetLimit)
        {
            return false;
        }

        return state.UsefulPendingUrls > 0
               || state.PagesQueued > 0
               || IsBatchLimitStop(state.LastBudgetStopReason)
               || state.MoreEligibleWorkRemained;
    }

    public static bool IsBatchLimitStop(string? reason)
        => string.Equals(reason, FetchBatchLimitReason, StringComparison.OrdinalIgnoreCase);

    public static bool IsDurationExceeded(ResearchDiscoveryState state, int maxResearchDurationMinutes)
    {
        var minutes = Math.Clamp(maxResearchDurationMinutes, 1, 240);
        return DateTimeOffset.UtcNow - state.StartedAtUtc > TimeSpan.FromMinutes(minutes);
    }

    public static bool IsSearchOnlyLimitReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.Contains("search request", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("search quer", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("All search queries", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("search provider unavailable", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("search provider unhealthy", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCatalogGapReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.Contains("no discovery sources matched", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("no relevant discovery sources", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("country-only catalog fallback", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("discovery.no_relevant_sources", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("discovery.no_candidates", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRunLevelLimitReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.Contains("duration", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("candidates limit", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldDeferYieldRecoveryForQueuedWork(
        ResearchDiscoveryState state,
        int eligibleScheduledCount,
        int relevantScheduledCount = -1)
    {
        if (state.YieldRecoveryAttempted)
        {
            return true;
        }

        if (state.FetchBudgetLimit > 0 && state.PagesFetched >= state.FetchBudgetLimit)
        {
            return true;
        }

        if (relevantScheduledCount >= 0)
        {
            return relevantScheduledCount > 0;
        }

        return eligibleScheduledCount > 0;
    }

    public static int CountRelevantScheduledWork(IReadOnlyList<CrawlScheduleItem> scheduled)
    {
        var count = 0;
        foreach (var item in scheduled)
        {
            if (item.Priority.Relevance > 0
                || item.Priority.IsDiscoveryUseful
                || item.Priority.ReasonCode.Contains("contact", StringComparison.OrdinalIgnoreCase)
                || item.Priority.ReasonCode.Contains("profile", StringComparison.OrdinalIgnoreCase))
            {
                if (item.Priority.ReasonCode.Contains("off_industry", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                count++;
            }
        }

        return count;
    }
}
