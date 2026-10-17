using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public class OutreachPrepareRequest
{
    public Guid ProjectId { get; set; }

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

public class OutreachPrepareResult
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

public interface IOutreachChannel
{
    OutreachChannelType Channel { get; }

    Task<OutreachPrepareResult> PrepareAsync(
        OutreachPrepareRequest request,
        CancellationToken cancellationToken);
}
