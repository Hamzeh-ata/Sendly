using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public static class ResearchRunOutcomeResolver
{
    public static ResearchRunStatus Resolve(
        int qualifiedCount,
        int requestedCount,
        ResearchDiscoveryState state,
        int providerFailureCount,
        bool allSourcesCompleted)
    {
        if (requestedCount > 0 && qualifiedCount >= requestedCount)
        {
            return providerFailureCount > 0
                ? ResearchRunStatus.CompletedWithFailures
                : ResearchRunStatus.Completed;
        }

        if (IsBudgetExhausted(state))
        {
            return ResearchRunStatus.BudgetExhausted;
        }

        return ResearchRunStatus.Partial;
    }

    public static bool IsBudgetExhausted(ResearchDiscoveryState state)
    {
        if (state.DiscoveryStopReason == DiscoveryStopReason.BudgetExhausted)
        {
            return true;
        }

        if (state.FetchBudgetLimit > 0 && state.PagesFetched >= state.FetchBudgetLimit)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(state.LimitsExhaustedReason)
            && state.LimitsExhaustedReason.Contains("duration", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return state.LimitsExhausted
               && !string.IsNullOrWhiteSpace(state.LastBudgetStopReason)
               && state.LastBudgetStopReason.StartsWith("fetch.", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTerminal(ResearchRunStatus status)
    {
        return status is ResearchRunStatus.Completed
            or ResearchRunStatus.CompletedWithFailures
            or ResearchRunStatus.Failed
            or ResearchRunStatus.Cancelled
            or ResearchRunStatus.Partial
            or ResearchRunStatus.BudgetExhausted;
    }
}
