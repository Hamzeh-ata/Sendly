using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

[Index(nameof(ProjectId), nameof(NormalizedCompanyName))]
[Index(nameof(ProjectId), nameof(NormalizedEmail), IsUnique = true)]
[Index(nameof(ProjectId), nameof(NormalizedWhatsApp), IsUnique = true)]
[Index(nameof(ProjectId), nameof(NormalizedWebsite))]
[Index(nameof(ProjectId), nameof(NormalizedPhone))]
[Index(nameof(NormalizedEmail))]
[Index(nameof(NormalizedWhatsApp))]
[Index(nameof(NormalizedWebsite))]
public class Prospect
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public OutreachProject? Project { get; set; }

    public Guid? ResearchRunId { get; set; }

    public ResearchRun? ResearchRun { get; set; }

    [Required]
    [MaxLength(300)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(300)]
    public string NormalizedCompanyName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Website { get; set; }

    [MaxLength(255)]
    public string? NormalizedWebsite { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? NormalizedCity { get; set; }

    [MaxLength(200)]
    public string? Industry { get; set; }

    [MaxLength(2000)]
    public string? CompanyDescription { get; set; }

    [MaxLength(100)]
    public string? CompanySize { get; set; }

    [MaxLength(200)]
    public string? ContactName { get; set; }

    [MaxLength(200)]
    public string? ContactRole { get; set; }

    [MaxLength(320)]
    public string? Email { get; set; }

    [MaxLength(320)]
    public string? NormalizedEmail { get; set; }

    [MaxLength(2000)]
    public string? AdditionalEmailsJson { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(50)]
    public string? NormalizedPhone { get; set; }

    [MaxLength(2000)]
    public string? AdditionalPhonesJson { get; set; }

    [MaxLength(50)]
    public string? WhatsApp { get; set; }

    [MaxLength(50)]
    public string? NormalizedWhatsApp { get; set; }

    [MaxLength(500)]
    public string? LinkedInUrl { get; set; }

    [MaxLength(1000)]
    public string? SourceUrl { get; set; }

    [MaxLength(4000)]
    public string? SourceUrlsJson { get; set; }

    public ProspectSourceType SourceType { get; set; } = ProspectSourceType.Manual;

    [MaxLength(200)]
    public string? SourceName { get; set; }

    [MaxLength(4000)]
    public string? ResearchNotes { get; set; }

    [MaxLength(1000)]
    public string? QualificationSummary { get; set; }

    public int? QualificationScore { get; set; }

    public int? EmailConfidenceScore { get; set; }

    public int? WhatsAppConfidenceScore { get; set; }

    public bool IsQualified { get; set; }

    public ProspectStatus Status { get; set; } = ProspectStatus.Discovered;

    public ProspectDuplicateKind DuplicateKind { get; set; } = ProspectDuplicateKind.None;

    public Guid? RecommendedTemplateId { get; set; }

    [MaxLength(500)]
    public string? RecommendedTemplateReason { get; set; }

    public int? RecommendedTemplateConfidence { get; set; }

    public Guid? OutreachCampaignId { get; set; }

    public string? PersonalizationJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
