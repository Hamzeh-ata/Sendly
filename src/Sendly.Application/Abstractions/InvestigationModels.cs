using Sendly.Application.Services;
using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public enum InvestigationSiteRole
{
    Unknown = 0,
    Directory = 1,
    Organization = 2
}

public enum InvestigationPageKind
{
    Unknown = 0,
    Home = 1,
    Category = 2,
    Listing = 3,
    Profile = 4,
    Contact = 5,
    About = 6,
    Team = 7,
    Location = 8,
    News = 9,
    Pagination = 10,
    Services = 11
}

public enum OrganizationInvestigationPhase
{
    Discovered = 0,
    IdentityEvidenceCollected = 1,
    IdentityVerified = 2,
    IndustryFitEvaluated = 3,
    ContactEvidenceCollected = 4,
    ContactabilityEvaluated = 5,
    Qualified = 6,
    Rejected = 7,
    EmailReady = 8,
    NotEmailReady = 9
}

public class DomainInvestigationPlan
{
    public string Domain { get; set; } = string.Empty;

    public InvestigationSiteRole Role { get; set; } = InvestigationSiteRole.Unknown;

    public int PagesInvestigated { get; set; }

    public int OrganizationsDiscovered { get; set; }

    public int UsefulEvidencePages { get; set; }

    public int CategoryPagesQueued { get; set; }

    public int ProfilePagesQueued { get; set; }

    public int OfficialDomainsQueued { get; set; }

    public int AdaptivePageAllowance { get; set; }

    public HashSet<string> SeenPageKinds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> CompletedInvestigationPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> PendingInvestigationPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsHighYield =>
        OrganizationsDiscovered >= 2
        || UsefulEvidencePages >= 2
        || (Role == InvestigationSiteRole.Directory && ProfilePagesQueued >= 3);

    public void RememberPageKind(InvestigationPageKind kind)
    {
        SeenPageKinds.Add(kind.ToString());
    }
}

public class OrganizationInvestigationState
{
    public string OrganizationKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? OfficialWebsiteUrl { get; set; }

    public string? OfficialDomain { get; set; }

    public string? ProfileUrl { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public List<string> AdditionalEmails { get; set; } = [];

    public List<string> AdditionalPhones { get; set; } = [];

    public string? SourceDirectoryUrl { get; set; }

    public OrganizationInvestigationPhase Phase { get; set; } = OrganizationInvestigationPhase.Discovered;

    public List<string> EvidenceUrls { get; set; } = [];

    public HashSet<string> CompletedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> PendingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int NameConfidence { get; set; }

    public bool IdentityVerified =>
        Phase is OrganizationInvestigationPhase.IdentityVerified
            or OrganizationInvestigationPhase.IndustryFitEvaluated
            or OrganizationInvestigationPhase.ContactEvidenceCollected
            or OrganizationInvestigationPhase.ContactabilityEvaluated
            or OrganizationInvestigationPhase.Qualified
            or OrganizationInvestigationPhase.EmailReady
            or OrganizationInvestigationPhase.NotEmailReady;

    public void AddEvidenceUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!EvidenceUrls.Contains(url, StringComparer.OrdinalIgnoreCase))
        {
            EvidenceUrls.Add(url);
        }
    }
}

public class InvestigationFollowUp
{
    public string Url { get; set; } = string.Empty;

    public string ReasonCode { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;

    public InvestigationPageKind ExpectedPageKind { get; set; }

    public int PriorityBoost { get; set; }

    public string? OrganizationKey { get; set; }

    public bool IsExternalOfficialDomain { get; set; }
}

public class ListingOrganization
{
    public string Name { get; set; } = string.Empty;

    public string? ProfileUrl { get; set; }

    public string? OfficialWebsiteUrl { get; set; }

    public string? Email { get; set; }

    public string? EmailOriginal { get; set; }

    public string? Phone { get; set; }

    public string? PhoneOriginal { get; set; }

    public string? WhatsApp { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? CountryEvidenceSource { get; set; }

    public string ExtractionMethod { get; set; } = "listing.unknown";

    public string SourcePageUrl { get; set; } = string.Empty;

    public int Confidence { get; set; }
}

public class PageInvestigationResult
{
    public InvestigationSiteRole SiteRole { get; set; }

    public InvestigationPageKind PageKind { get; set; }

    public List<ListingOrganization> Listings { get; set; } = [];

    public List<InvestigationFollowUp> FollowUps { get; set; } = [];

    public List<ProspectCandidate> OrganizationCandidates { get; set; } = [];

    public List<ExtractedPageFact> EvidenceFacts { get; set; } = [];

    public int CategoryLinksFound { get; set; }

    public int ProfileLinksFound { get; set; }

    public int OfficialDomainsFound { get; set; }

    public bool YieldedUsefulEvidence { get; set; }

    public string Summary { get; set; } = string.Empty;
}

public class FetchedPageInvestigationRequest
{
    public string PageUrl { get; set; } = string.Empty;

    public string? NormalizedDomain { get; set; }

    public string Html { get; set; } = string.Empty;

    public ExtractedPageData Extracted { get; set; } = new();

    public UrlPagePurpose UrlPurpose { get; set; }

    public DiscoverySourceType SourceType { get; set; }

    public bool IsDirectorySeed { get; set; }

    public int Depth { get; set; }

    public int MaxDepth { get; set; }

    public IReadOnlyList<string> Industries { get; set; } = [];

    public IReadOnlyList<string> Countries { get; set; } = [];

    public ResearchDiscoveryState DiscoveryState { get; set; } = new();
}
