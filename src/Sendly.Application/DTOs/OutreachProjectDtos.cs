using Sendly.Domain.Enums;

namespace Sendly.Application.DTOs;

public class ProjectTemplateLinkDto
{
    public Guid? Id { get; set; }

    public Guid TemplateId { get; set; }

    public string? TemplateName { get; set; }

    public OutreachChannelType Channel { get; set; } = OutreachChannelType.Email;

    public string? MatchIndustry { get; set; }

    public string? MatchCompanySize { get; set; }

    public string? MatchNotes { get; set; }

    public int SortOrder { get; set; }
}

public class UpsertOutreachProjectDto
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ResearchInstructions { get; set; }

    public bool IsActive { get; set; } = true;

    public int MinQualificationScore { get; set; } = 70;

    public DuplicateHandlingMode DuplicateHandling { get; set; } = DuplicateHandlingMode.Exclude;

    public bool EnableEmail { get; set; } = true;

    public bool EnableWhatsApp { get; set; }

    public int DefaultRequestedProspectCount { get; set; } = 50;

    public List<string> Keywords { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];

    public List<string> Countries { get; set; } = [];

    public List<string> Industries { get; set; } = [];

    public List<string> ExcludedIndustries { get; set; } = [];

    public List<string> TargetRoles { get; set; } = [];

    public List<ProjectTemplateLinkDto> Templates { get; set; } = [];
}

public class OutreachProjectSummaryDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ResearchInstructions { get; set; }

    public bool IsActive { get; set; }

    public bool EnableEmail { get; set; }

    public bool EnableWhatsApp { get; set; }

    public List<string> Countries { get; set; } = [];

    public List<string> Keywords { get; set; } = [];

    public int ProspectCount { get; set; }

    public int QualifiedCount { get; set; }

    public int NeedsReviewCount { get; set; }

    public int RejectedCount { get; set; }

    public int ValidEmailCount { get; set; }

    public int ValidWhatsAppCount { get; set; }

    public int AlreadyContactedCount { get; set; }

    public int ReadyForOutreachCount { get; set; }

    public int CampaignsCreatedCount { get; set; }

    public DateTimeOffset? LastResearchAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public class OutreachProjectDetailDto : OutreachProjectSummaryDto
{
    public int MinQualificationScore { get; set; }

    public DuplicateHandlingMode DuplicateHandling { get; set; }

    public int DefaultRequestedProspectCount { get; set; }

    public List<string> ExcludedKeywords { get; set; } = [];

    public List<string> Industries { get; set; } = [];

    public List<string> ExcludedIndustries { get; set; } = [];

    public List<string> TargetRoles { get; set; } = [];

    public List<ProjectTemplateLinkDto> Templates { get; set; } = [];

    public List<ResearchRunSummaryDto> RecentResearchRuns { get; set; } = [];
}
