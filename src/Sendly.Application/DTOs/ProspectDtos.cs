using Sendly.Domain.Enums;

namespace Sendly.Application.DTOs;

public class ProspectSummaryDto
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string? ProjectName { get; set; }

    public Guid? ResearchRunId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string? Website { get; set; }

    public string? Country { get; set; }

    public string? City { get; set; }

    public string? Industry { get; set; }

    public string? CompanySize { get; set; }

    public string? ContactName { get; set; }

    public string? ContactRole { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? WhatsApp { get; set; }

    public string? SourceUrl { get; set; }

    public List<string> SourceUrls { get; set; } = [];

    public ProspectSourceType SourceType { get; set; }

    public string? SourceName { get; set; }

    public int? QualificationScore { get; set; }

    public int? EmailConfidenceScore { get; set; }

    public int? WhatsAppConfidenceScore { get; set; }

    public bool IsQualified { get; set; }

    public ProspectStatus Status { get; set; }

    public ProspectDuplicateKind DuplicateKind { get; set; }

    public Guid? RecommendedTemplateId { get; set; }

    public string? RecommendedTemplateName { get; set; }

    public string? RecommendedTemplateReason { get; set; }

    public int? RecommendedTemplateConfidence { get; set; }

    public Guid? OutreachCampaignId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public class ProspectDetailDto : ProspectSummaryDto
{
    public string? CompanyDescription { get; set; }

    public string? LinkedInUrl { get; set; }

    public List<string> AdditionalEmails { get; set; } = [];

    public List<string> AdditionalPhones { get; set; } = [];

    public string? ResearchNotes { get; set; }

    public string? QualificationSummary { get; set; }

    public List<OutreachContactHistoryDto> ContactHistory { get; set; } = [];
}

public class OutreachContactHistoryDto
{
    public Guid Id { get; set; }

    public OutreachChannelType Channel { get; set; }

    public string ContactValue { get; set; } = string.Empty;

    public Guid? CampaignId { get; set; }

    public Guid? TemplateId { get; set; }

    public DateTimeOffset ContactedAtUtc { get; set; }
}

public class CreateProspectDto
{
    public Guid ProjectId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string? Website { get; set; }

    public string? Country { get; set; }

    public string? City { get; set; }

    public string? Industry { get; set; }

    public string? CompanyDescription { get; set; }

    public string? CompanySize { get; set; }

    public string? ContactName { get; set; }

    public string? ContactRole { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? WhatsApp { get; set; }

    public string? LinkedInUrl { get; set; }

    public string? SourceUrl { get; set; }

    public ProspectSourceType SourceType { get; set; } = ProspectSourceType.Manual;

    public string? SourceName { get; set; }

    public string? ResearchNotes { get; set; }
}

public class ProspectFilterDto
{
    public Guid? ProjectId { get; set; }

    public Guid[]? ProjectIds { get; set; }

    public Guid? ResearchRunId { get; set; }

    public string? Search { get; set; }

    public string? Country { get; set; }

    public string? Industry { get; set; }

    public string[]? ContactMethods { get; set; }

    public string[]? Statuses { get; set; }

    public string[]? Qualifications { get; set; }

    public string[]? Duplicates { get; set; }
}

public class RecommendTemplateRequestDto
{
    public Guid? TemplateId { get; set; }
}

public class RecommendTemplateResponseDto
{
    public Guid? TemplateId { get; set; }

    public string? TemplateName { get; set; }

    public string? Reason { get; set; }

    public int Confidence { get; set; }
}

public class PrepareOutreachRequestDto
{
    public List<Guid> ProspectIds { get; set; } = [];

    public OutreachChannelType Channel { get; set; } = OutreachChannelType.Email;

    public Guid? TemplateId { get; set; }

    public bool UseAiRecommendedTemplate { get; set; }

    public Guid? EmailAccountId { get; set; }

    public Guid? SignatureId { get; set; }

    public bool AllowAlreadyContacted { get; set; }

    public int? DelayMilliseconds { get; set; }

    public int? BatchSize { get; set; }

    public int? MaxRetryCount { get; set; }
}

public class PrepareOutreachResponseDto
{
    public OutreachChannelType Channel { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid? TemplateId { get; set; }

    public string? TemplateName { get; set; }

    public int SelectedCount { get; set; }

    public int PreparedCount { get; set; }

    public int SkippedDuplicateCount { get; set; }

    public int SkippedInvalidCount { get; set; }

    public List<string> Warnings { get; set; } = [];

    public List<Guid> PreparedProspectIds { get; set; } = [];
}

public class MergeProspectsRequestDto
{
    public Guid TargetProspectId { get; set; }

    public Guid SourceProspectId { get; set; }
}
