using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

[Index(nameof(ResearchRunId), nameof(CreatedAtUtc))]
[Index(nameof(ResearchRunId), nameof(Severity))]
[Index(nameof(ResearchRunId), nameof(Stage))]
public class ResearchRunEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ResearchRunId { get; set; }

    public ResearchRun? ResearchRun { get; set; }

    public ResearchRunEventType EventType { get; set; }

    public ResearchRunEventSeverity Severity { get; set; } = ResearchRunEventSeverity.Info;

    public ResearchPipelineStage Stage { get; set; } = ResearchPipelineStage.RunLifecycle;

    [MaxLength(1000)]
    public string? Message { get; set; }

    [MaxLength(8000)]
    public string? DataJson { get; set; }

    public int? DurationMs { get; set; }

    public Guid? RelatedEntityId { get; set; }

    [MaxLength(32)]
    public string? RelatedEntityKind { get; set; }

    [MaxLength(255)]
    public string? Domain { get; set; }

    [MaxLength(2000)]
    public string? Url { get; set; }

    public int? HttpStatusCode { get; set; }

    [MaxLength(100)]
    public string? Provider { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
