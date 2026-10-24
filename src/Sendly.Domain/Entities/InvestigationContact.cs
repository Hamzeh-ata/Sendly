using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Domain.Entities;

[Index(nameof(ResearchRunId))]
[Index(nameof(ProspectId), nameof(NormalizedValue))]
public class InvestigationContact
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ResearchRunId { get; set; }

    public ResearchRun? ResearchRun { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? ProspectId { get; set; }

    public Prospect? Prospect { get; set; }

    [MaxLength(300)]
    public string? OrganizationName { get; set; }

    [Required]
    [MaxLength(500)]
    public string ExactValue { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? NormalizedValue { get; set; }

    [Required]
    [MaxLength(40)]
    public string ContactType { get; set; } = "email";

    [MaxLength(2000)]
    public string? SourceUrl { get; set; }

    [MaxLength(500)]
    public string? PageTitle { get; set; }

    [MaxLength(1000)]
    public string? SurroundingText { get; set; }

    [MaxLength(1000)]
    public string? AttributionEvidence { get; set; }

    [MaxLength(40)]
    public string ValidationStatus { get; set; } = "extracted";

    public int Confidence { get; set; }

    [MaxLength(40)]
    public string ReviewStatus { get; set; } = "unreviewed";

    public DateTimeOffset DiscoveredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
