using Sendly.Domain.Entities;
using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public class ProspectCandidate
{
    public string CompanyName { get; set; } = string.Empty;

    public string? Website { get; set; }

    public string? OfficialDomain { get; set; }

    public string? Country { get; set; }

    public string? CountryEvidenceSource { get; set; }

    public string? CountryEvidenceSummary { get; set; }

    public string? CountryMatchReasonCode { get; set; }

    public string? City { get; set; }

    public string? Industry { get; set; }

    public string? CompanyDescription { get; set; }

    public string? CompanySize { get; set; }

    public string? ContactName { get; set; }

    public string? ContactRole { get; set; }

    public string? Email { get; set; }

    public List<string> AdditionalEmails { get; set; } = [];

    public List<Domain.Services.AttributedContact> AttributedEmails { get; set; } = [];

    public string? Phone { get; set; }

    public List<string> AdditionalPhones { get; set; } = [];

    public List<Domain.Services.AttributedContact> AttributedPhones { get; set; } = [];

    public string? WhatsApp { get; set; }

    public string? Address { get; set; }

    public string? LinkedInUrl { get; set; }

    public string? SourceUrl { get; set; }

    public List<string> AdditionalSourceUrls { get; set; } = [];

    public ProspectSourceType SourceType { get; set; } = ProspectSourceType.Other;

    public string? SourceName { get; set; }

    public string? ResearchNotes { get; set; }

    public ResearchEntityType EntityType { get; set; } = ResearchEntityType.Unknown;

    public int EntityTypeConfidence { get; set; }

    public string? EntityTypeReasonCode { get; set; }

    public int NameConfidence { get; set; }

    public string? NameReasonCode { get; set; }

    public string? ContactEvidenceUrl { get; set; }

    public string? ContactPageDomain { get; set; }

    public ContactAttributionState ContactAttributionState { get; set; } = ContactAttributionState.Unverified;

    public int ContactAttributionConfidence { get; set; }

    public string? ContactAttributionReasonCode { get; set; }
}

public class ProspectSourceContext
{
    public Guid ProjectId { get; set; }

    public Guid ResearchRunId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ResearchInstructions { get; set; }

    public List<string> Keywords { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];

    public List<string> Countries { get; set; } = [];

    public List<string> Industries { get; set; } = [];

    public List<string> ExcludedIndustries { get; set; } = [];

    public List<string> TargetRoles { get; set; } = [];

    public List<string> Locations { get; set; } = [];

    public ResearchSpecification? Specification { get; set; }

    public ResearchQueryVersion? QueryVersion { get; set; }

    public bool IncludeEmail { get; set; } = true;

    public bool IncludeWhatsApp { get; set; }

    public int RequestedCount { get; set; }

    public int DiscoveryBatchSize { get; set; } = 25;

    public ResearchDiscoveryState DiscoveryState { get; set; } = new();

    public Func<ResearchDiscoveryState, CancellationToken, Task>? ReportProgressAsync { get; set; }
}

public interface IProspectSource
{
    string Name { get; }

    bool IsEnabled { get; }

    Task<IReadOnlyList<ProspectCandidate>> DiscoverAsync(
        ProspectSourceContext context,
        CancellationToken cancellationToken);
}
