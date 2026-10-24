namespace Sendly.Application.Abstractions;

public class AiSearchResultClassificationRequest
{
    public string? Title { get; set; }
    public string? Snippet { get; set; }
    public string? Url { get; set; }
    public string? Query { get; set; }
    public List<string> Industries { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    public List<string> Locations { get; set; } = [];
    public List<string> ExcludedIndustries { get; set; } = [];
}

public class AiSearchResultClassification
{
    public string Decision { get; set; } = "Ambiguous";
    public string? BusinessType { get; set; }
    public string? CountryEvidence { get; set; }
    public string? IndustryEvidence { get; set; }
    public int Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<string> EvidenceQuotes { get; set; } = [];
    public bool NeedsVerification { get; set; } = true;
    public bool IsValid { get; set; }
}

public class AiBusinessVerificationRequest
{
    public string? CompanyName { get; set; }
    public string? Url { get; set; }
    public string? PageTitle { get; set; }
    public string? PageText { get; set; }
    public List<string> Industries { get; set; } = [];
    public List<string> Countries { get; set; } = [];
}

public class AiBusinessVerification
{
    public bool IsRealBusiness { get; set; }
    public bool IsOfficialWebsite { get; set; }
    public bool MatchesIndustry { get; set; }
    public bool MatchesCountry { get; set; }
    public string? BusinessName { get; set; }
    public string? OfficialWebsite { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Industry { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? ContactPage { get; set; }
    public int Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<string> EvidenceQuotes { get; set; } = [];
    public bool IsValid { get; set; }
}

public interface ILocalAiSearchClassifier
{
    Task<AiSearchResultClassification> ClassifySearchResultAsync(
        AiSearchResultClassificationRequest request,
        CancellationToken cancellationToken);

    Task<AiBusinessVerification> VerifyBusinessAsync(
        AiBusinessVerificationRequest request,
        CancellationToken cancellationToken);
}
