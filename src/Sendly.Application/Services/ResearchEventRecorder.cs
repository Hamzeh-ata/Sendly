using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public class ResearchEventRecorder : IResearchEventRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ISendlyDbContext _db;
    private readonly IResearchProgressNotifier _notifier;

    public ResearchEventRecorder(ISendlyDbContext db, IResearchProgressNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    public async Task<ResearchRunEventDto> RecordAsync(
        ResearchEventRecordRequest request,
        CancellationToken cancellationToken)
    {
        var entity = new ResearchRunEvent
        {
            ResearchRunId = request.ResearchRunId,
            EventType = request.EventType,
            Severity = request.Severity,
            Stage = request.Stage,
            Message = Truncate(request.Message, 1000),
            DataJson = SerializeData(request.Data),
            DurationMs = request.DurationMs,
            RelatedEntityId = request.RelatedEntityId,
            RelatedEntityKind = Truncate(request.RelatedEntityKind, 32),
            Domain = Truncate(request.Domain, 255),
            Url = Truncate(request.Url, 2000),
            HttpStatusCode = request.HttpStatusCode,
            Provider = Truncate(request.Provider, 100),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.ResearchRunEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        var dto = Map(entity);
        await _notifier.NotifyEventAsync(dto, cancellationToken);
        return dto;
    }

    public static ResearchRunEventDto Map(ResearchRunEvent entity)
    {
        return new ResearchRunEventDto
        {
            Id = entity.Id,
            ResearchRunId = entity.ResearchRunId,
            EventType = entity.EventType.ToString(),
            Severity = entity.Severity.ToString(),
            Stage = entity.Stage.ToString(),
            Message = entity.Message,
            DataJson = entity.DataJson,
            DurationMs = entity.DurationMs,
            RelatedEntityId = entity.RelatedEntityId,
            RelatedEntityKind = entity.RelatedEntityKind,
            Domain = entity.Domain,
            Url = entity.Url,
            HttpStatusCode = entity.HttpStatusCode,
            Provider = entity.Provider,
            CreatedAtUtc = entity.CreatedAtUtc
        };
    }

    private static string? SerializeData(object? data)
    {
        if (data is null)
        {
            return null;
        }

        try
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            return Truncate(json, 8000);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
