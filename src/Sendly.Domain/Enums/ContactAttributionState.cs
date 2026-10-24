namespace Sendly.Domain.Enums;

public enum ContactAttributionState
{
    Unverified = 0,
    SameDomain = 1,
    CrossDomainEvidence = 2,
    DirectoryListing = 3,
    Rejected = 4
}
