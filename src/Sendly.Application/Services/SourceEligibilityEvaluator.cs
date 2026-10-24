using Sendly.Application.Abstractions;

namespace Sendly.Application.Services;

public static class SourceEligibilityEvaluator
{
    private static readonly HashSet<string> SecondaryRegistryKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Registry",
        "Archive",
        "Marketplace"
    };

    public static SourceEligibilityDecision Evaluate(
        string sourceName,
        string? sourceUrl,
        string sourceKind,
        IReadOnlyList<string> sourceCountries,
        IReadOnlyList<string> sourceIndustries,
        IReadOnlyList<string> targetCountries,
        IReadOnlyList<string> targetIndustries,
        bool automatedAccessPermitted = true,
        bool temporarilyUnavailable = false)
    {
        if (temporarilyUnavailable)
        {
            return Decision(
                sourceName,
                sourceUrl,
                sourceKind,
                SourceEligibilityState.TemporarilyUnavailable,
                "source.temporarily_unavailable",
                "Source is temporarily unavailable or deprioritized.",
                countryMatched: false,
                industryMatched: false,
                countsAsRelevant: false,
                canReveal: false,
                value: 0);
        }

        if (!automatedAccessPermitted)
        {
            return Decision(
                sourceName,
                sourceUrl,
                sourceKind,
                SourceEligibilityState.Ineligible,
                "source.access_not_permitted",
                "Automated access is not permitted for this source.",
                countryMatched: false,
                industryMatched: false,
                countsAsRelevant: false,
                canReveal: false,
                value: 0);
        }

        var countryMatched = targetCountries.Count == 0
                             || IndustrySynonymCatalog.MatchesCountryScope(targetCountries, sourceCountries);
        if (!countryMatched)
        {
            return Decision(
                sourceName,
                sourceUrl,
                sourceKind,
                SourceEligibilityState.Ineligible,
                "source.country_mismatch",
                "Source country coverage does not include the target country.",
                countryMatched: false,
                industryMatched: false,
                countsAsRelevant: false,
                canReveal: false,
                value: 0);
        }

        if (targetIndustries.Count == 0)
        {
            return Decision(
                sourceName,
                sourceUrl,
                sourceKind,
                SourceEligibilityState.UnknownNeedsEvaluation,
                "source.unknown_needs_evaluation",
                "No target industries were supplied; source relevance cannot be confirmed.",
                countryMatched: true,
                industryMatched: false,
                countsAsRelevant: false,
                canReveal: true,
                value: 10);
        }

        if (sourceIndustries.Count == 0)
        {
            return Decision(
                sourceName,
                sourceUrl,
                sourceKind,
                SourceEligibilityState.UnknownNeedsEvaluation,
                "source.missing_industry_metadata",
                "Source has no industry metadata; relevance is unknown and it was not admitted.",
                countryMatched: true,
                industryMatched: false,
                countsAsRelevant: false,
                canReveal: false,
                value: 0);
        }

        var industryMatch = IndustrySynonymCatalog.FindIndustryOverlap(targetIndustries, sourceIndustries);
        if (industryMatch is null)
        {
            return Decision(
                sourceName,
                sourceUrl,
                sourceKind,
                SourceEligibilityState.Ineligible,
                "source.industry_mismatch",
                "Source industries do not overlap the target industry or its supported synonyms. Country match alone is not sufficient.",
                countryMatched: true,
                industryMatched: false,
                countsAsRelevant: false,
                canReveal: false,
                value: 0);
        }

        var isSecondary = SecondaryRegistryKinds.Contains(sourceKind);
        if (isSecondary)
        {
            if (IsSchoolFocused(targetIndustries))
            {
                return Decision(
                    sourceName,
                    sourceUrl,
                    sourceKind,
                    SourceEligibilityState.Ineligible,
                    "source.registry_low_value_for_schools",
                    "Generic company registries are not used as primary discovery for school queries; prefer school directories and associations.",
                    countryMatched: true,
                    industryMatched: true,
                    countsAsRelevant: false,
                    canReveal: false,
                    value: 0);
            }

            return new SourceEligibilityDecision
            {
                SourceName = sourceName,
                SourceUrl = sourceUrl,
                SourceKind = sourceKind,
                State = SourceEligibilityState.EligibleSecondary,
                ReasonCode = "source.eligible_secondary_registry",
                Explanation =
                    $"Generic {sourceKind.ToLowerInvariant()} may reveal target organizations but is not a relevant industry directory. Matched via '{industryMatch}'.",
                CountryMatched = true,
                IndustryMatched = true,
                MatchedIndustryEvidence = industryMatch,
                CountsAsRelevantIndustrySource = false,
                CanRevealTargetOrganizations = true,
                ExpectedDiscoveryValue = 25
            };
        }

        return new SourceEligibilityDecision
        {
            SourceName = sourceName,
            SourceUrl = sourceUrl,
            SourceKind = sourceKind,
            State = SourceEligibilityState.Eligible,
            ReasonCode = "source.eligible_industry_directory",
            Explanation =
                $"Source matches target country and industry (evidence: '{industryMatch}') and can reveal individual target organizations.",
            CountryMatched = true,
            IndustryMatched = true,
            MatchedIndustryEvidence = industryMatch,
            CountsAsRelevantIndustrySource = true,
            CanRevealTargetOrganizations = true,
            ExpectedDiscoveryValue = 70
        };
    }

    public static SourceEligibilityResolveResult ResolveMany(
        IEnumerable<(string Name, string? Url, string Kind, IReadOnlyList<string> Countries, IReadOnlyList<string> Industries, bool AccessPermitted, bool TemporarilyUnavailable)> sources,
        IReadOnlyList<string> targetCountries,
        IReadOnlyList<string> targetIndustries)
    {
        var result = new SourceEligibilityResolveResult();
        foreach (var source in sources)
        {
            var decision = Evaluate(
                source.Name,
                source.Url,
                source.Kind,
                source.Countries,
                source.Industries,
                targetCountries,
                targetIndustries,
                source.AccessPermitted,
                source.TemporarilyUnavailable);
            result.Decisions.Add(decision);
            if (decision.State == SourceEligibilityState.Eligible)
            {
                result.EligiblePrimary.Add(decision);
            }
            else if (decision.State == SourceEligibilityState.EligibleSecondary)
            {
                result.EligibleSecondary.Add(decision);
            }
        }

        return result;
    }

    private static bool IsSchoolFocused(IReadOnlyList<string> industries)
    {
        foreach (var industry in industries)
        {
            if (string.IsNullOrWhiteSpace(industry))
            {
                continue;
            }

            if (industry.Contains("school", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("education", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("academy", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("preparatory", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static SourceEligibilityDecision Decision(
        string name,
        string? url,
        string kind,
        SourceEligibilityState state,
        string reasonCode,
        string explanation,
        bool countryMatched,
        bool industryMatched,
        bool countsAsRelevant,
        bool canReveal,
        int value)
    {
        return new SourceEligibilityDecision
        {
            SourceName = name,
            SourceUrl = url,
            SourceKind = kind,
            State = state,
            ReasonCode = reasonCode,
            Explanation = explanation,
            CountryMatched = countryMatched,
            IndustryMatched = industryMatched,
            CountsAsRelevantIndustrySource = countsAsRelevant,
            CanRevealTargetOrganizations = canReveal,
            ExpectedDiscoveryValue = value
        };
    }
}
