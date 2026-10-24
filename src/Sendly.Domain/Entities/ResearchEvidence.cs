using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

[Index(nameof(ProspectId), nameof(FactType))]
[Index(nameof(ResearchRunId))]
public class ResearchEvidence
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public Guid ResearchRunId { get; set; }

    public ResearchRun? ResearchRun { get; set; }

    public Guid? ProspectId { get; set; }

    public Prospect? Prospect { get; set; }

    public Guid? CrawledPageId { get; set; }

    public CrawledPage? CrawledPage { get; set; }

    [Required]
    [MaxLength(100)]
    public string FactType { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? FactValue { get; set; }

    public EvidenceConfidenceKind ConfidenceKind { get; set; } = EvidenceConfidenceKind.Verified;

    [MaxLength(2000)]
    public string? SourceUrl { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTimeOffset FirstDiscoveredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastVerifiedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
