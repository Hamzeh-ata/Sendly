namespace Sendly.Domain.Enums;

public enum ResearchCandidateDisposition
{
    Discovered = 0,
    RejectedWrongIndustry = 1,
    RejectedWrongCountry = 2,
    RejectedExcludedCategory = 3,
    RejectedVendorNotOperator = 4,
    RejectedUnrelated = 5,
    NeedsVerification = 6,
    IndustryVerified = 7,
    CountryVerified = 8,
    Qualified = 9,
    ContactIncomplete = 10,
    Duplicate = 11,
    ResearchFailed = 12,
    RejectedBeforeFetch = 13
}
