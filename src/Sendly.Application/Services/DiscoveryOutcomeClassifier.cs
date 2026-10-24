using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public static class DiscoveryOutcomeClassifier
{
    public static DiscoveryStopReason Classify(ResearchDiscoveryState state, int qualifiedCount, int requestedCount)
    {
        if (qualifiedCount >= requestedCount && requestedCount > 0)
        {
            return DiscoveryStopReason.TargetReached;
        }

        if (state.SearchResultsReceived == 0
            && (state.DiscoveryStopReason == DiscoveryStopReason.SearchProviderUnavailable
                || state.SearchProviderHealth is SearchProviderHealthState.Unavailable
                    or SearchProviderHealthState.InvalidResponse)
            && state.CandidatesDiscovered == 0)
        {
            return DiscoveryStopReason.SearchProviderUnavailable;
        }

        if (state.LimitsExhausted
            && !string.IsNullOrWhiteSpace(state.LimitsExhaustedReason)
            && state.LimitsExhaustedReason.Contains("duration", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoveryStopReason.BudgetExhausted;
        }

        var providersAttempted = state.AutomaticDiscoveryProviderStats.Count(x => x.Attempted);
        var providersFailed = state.AutomaticDiscoveryProviderStats.Count(x => x.Attempted && !x.Succeeded);
        if (providersAttempted > 0 && providersFailed == providersAttempted && state.CandidatesDiscovered == 0)
        {
            return DiscoveryStopReason.ProvidersFailed;
        }

        if (state.CandidatesDiscovered == 0)
        {
            return DiscoveryStopReason.NoCandidatesDiscovered;
        }

        if (state.CandidatesValidated == 0 && state.CandidatesDiscovered > 0)
        {
            if (HasRejection(state, "unavailable") || HasRejection(state, "fetch") || HasRejection(state, "robots"))
            {
                return DiscoveryStopReason.CandidatesUnavailable;
            }

            return DiscoveryStopReason.NoCompaniesIdentified;
        }

        if (qualifiedCount == 0 && state.CandidatesValidated > 0)
        {
            if (HasRejection(state, "email") || HasRejection(state, "website") || HasRejection(state, "mandatory"))
            {
                return DiscoveryStopReason.MissingRequiredEvidence;
            }

            return DiscoveryStopReason.NoQualifiedProspects;
        }

        if (state.LimitsExhausted)
        {
            return DiscoveryStopReason.BudgetExhausted;
        }

        return DiscoveryStopReason.Unproductive;
    }

    public static string Describe(DiscoveryStopReason reason)
    {
        return reason switch
        {
            DiscoveryStopReason.TargetReached => "Requested qualified prospect target was reached.",
            DiscoveryStopReason.NoCandidatesDiscovered =>
                "No candidate URLs were discovered by the configured automatic discovery strategies.",
            DiscoveryStopReason.NoRelevantDiscoverySources =>
                "No discovery sources matched the target industry and country. Country-only catalog fallback is disabled; alternative providers were attempted or reported as unavailable.",
            DiscoveryStopReason.CandidatesUnavailable =>
                "Candidate websites were discovered but could not be fetched or crawled.",
            DiscoveryStopReason.NoCompaniesIdentified =>
                "Pages were crawled but no companies were identified.",
            DiscoveryStopReason.MissingRequiredEvidence =>
                "Companies were identified but required evidence or mandatory criteria were not satisfied.",
            DiscoveryStopReason.NoQualifiedProspects =>
                "Companies were identified but none met the qualification criteria.",
            DiscoveryStopReason.ProvidersFailed =>
                "All attempted discovery providers failed. Detailed diagnostics were retained for retry.",
            DiscoveryStopReason.BudgetExhausted =>
                "The research budget or duration limit was exhausted before sufficient coverage was achieved.",
            DiscoveryStopReason.Cancelled => "Research run was cancelled.",
            DiscoveryStopReason.Unproductive =>
                "Additional discovery became unproductive within the configured budget.",
            DiscoveryStopReason.SearchProviderUnavailable =>
                "Search could not proceed because the search provider was unavailable or returned unusable responses.",
            _ => "Research completed."
        };
    }

    private static bool HasRejection(ResearchDiscoveryState state, string token)
    {
        foreach (var key in state.RejectionReasons.Keys)
        {
            if (key.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
