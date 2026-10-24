namespace Sendly.Application.Abstractions;

public enum SourceEligibilityState
{
    Eligible = 0,
    EligibleSecondary = 1,
    Ineligible = 2,
    UnknownNeedsEvaluation = 3,
    TemporarilyUnavailable = 4
}

public class SourceEligibilityDecision
{
    public string SourceName { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public string SourceKind { get; set; } = "Directory";

    public SourceEligibilityState State { get; set; } = SourceEligibilityState.Ineligible;

    public string ReasonCode { get; set; } = "source.ineligible";

    public string Explanation { get; set; } = string.Empty;

    public bool CountryMatched { get; set; }

    public bool IndustryMatched { get; set; }

    public string? MatchedIndustryEvidence { get; set; }

    public bool CountsAsRelevantIndustrySource { get; set; }

    public bool CanRevealTargetOrganizations { get; set; }

    public int ExpectedDiscoveryValue { get; set; }
}

public class SourceEligibilityResolveResult
{
    public List<SourceEligibilityDecision> Decisions { get; set; } = [];

    public List<SourceEligibilityDecision> EligiblePrimary { get; set; } = [];

    public List<SourceEligibilityDecision> EligibleSecondary { get; set; } = [];

    public int RelevantIndustrySourceCount => EligiblePrimary.Count;

    public int DistinctRelevantSourceKinds =>
        EligiblePrimary
            .Select(x => x.SourceKind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
}

public class DiscoverySourceResolveResult
{
    public List<Domain.Entities.DiscoverySource> Sources { get; set; } = [];

    public List<SourceEligibilityDecision> Decisions { get; set; } = [];

    public SourceEligibilityResolveResult Eligibility { get; set; } = new();

    public Dictionary<Guid, SourceEligibilityDecision> SourceDecisions { get; set; } = new();
}
