namespace Sendly.Domain.Enums;

public enum DiscoveryStopReason
{
    None = 0,
    TargetReached = 1,
    NoCandidatesDiscovered = 2,
    CandidatesUnavailable = 3,
    NoCompaniesIdentified = 4,
    MissingRequiredEvidence = 5,
    NoQualifiedProspects = 6,
    ProvidersFailed = 7,
    BudgetExhausted = 8,
    Cancelled = 9,
    Unproductive = 10,
    NoRelevantDiscoverySources = 11,
    SearchProviderUnavailable = 12
}
