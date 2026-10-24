using Sendly.Application.DTOs;
using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public class ResearchEventRecordRequest
{
    public Guid ResearchRunId { get; set; }

    public ResearchRunEventType EventType { get; set; }

    public ResearchRunEventSeverity Severity { get; set; } = ResearchRunEventSeverity.Info;

    public ResearchPipelineStage Stage { get; set; } = ResearchPipelineStage.RunLifecycle;

    public string? Message { get; set; }

    public object? Data { get; set; }

    public int? DurationMs { get; set; }

    public Guid? RelatedEntityId { get; set; }

    public string? RelatedEntityKind { get; set; }

    public string? Domain { get; set; }

    public string? Url { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? Provider { get; set; }
}

public interface IResearchEventRecorder
{
    Task<ResearchRunEventDto> RecordAsync(
        ResearchEventRecordRequest request,
        CancellationToken cancellationToken);
}
