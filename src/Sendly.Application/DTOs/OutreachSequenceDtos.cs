using Sendly.Domain.Enums;

namespace Sendly.Application.DTOs;

public class OutreachSequenceStepDto
{
    public Guid? Id { get; set; }

    public int StepOrder { get; set; }

    public OutreachChannelType Channel { get; set; } = OutreachChannelType.Email;

    public Guid? TemplateId { get; set; }

    public string? TemplateName { get; set; }

    public int DelayHoursAfterPrevious { get; set; }

    public string? WhatsAppTemplateName { get; set; }

    public string? WhatsAppTemplateLanguage { get; set; }

    public string? VariantKey { get; set; }

    public int TrafficPercent { get; set; } = 100;
}

public class UpsertOutreachSequenceDto
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public List<OutreachSequenceStepDto> Steps { get; set; } = [];
}

public class OutreachSequenceSummaryDto
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string? ProjectName { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public int StepCount { get; set; }

    public int ActiveEnrollmentCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public class OutreachSequenceDetailDto : OutreachSequenceSummaryDto
{
    public List<OutreachSequenceStepDto> Steps { get; set; } = [];
}

public class EnrollProspectsRequestDto
{
    public List<Guid> ProspectIds { get; set; } = [];
}

public class SequenceEnrollmentDto
{
    public Guid Id { get; set; }

    public Guid SequenceId { get; set; }

    public string? SequenceName { get; set; }

    public Guid ProspectId { get; set; }

    public string? ProspectCompanyName { get; set; }

    public Guid ProjectId { get; set; }

    public SequenceEnrollmentStatus Status { get; set; }

    public int CurrentStepOrder { get; set; }

    public string? AssignedVariantKey { get; set; }

    public DateTimeOffset? NextRunAtUtc { get; set; }

    public DateTimeOffset EnrolledAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? LastError { get; set; }
}

public class SequenceAnalyticsDto
{
    public Guid SequenceId { get; set; }

    public string SequenceName { get; set; } = string.Empty;

    public int TotalEnrollments { get; set; }

    public int ActiveCount { get; set; }

    public int CompletedCount { get; set; }

    public int FailedCount { get; set; }

    public int CancelledCount { get; set; }

    public double CompletionRate { get; set; }

    public List<SequenceVariantAnalyticsDto> Variants { get; set; } = [];

    public List<SequenceStepAnalyticsDto> Steps { get; set; } = [];
}

public class SequenceVariantAnalyticsDto
{
    public string VariantKey { get; set; } = string.Empty;

    public int EnrollmentCount { get; set; }

    public int CompletedCount { get; set; }

    public int FailedCount { get; set; }

    public int ActiveCount { get; set; }

    public double CompletionRate { get; set; }
}

public class SequenceStepAnalyticsDto
{
    public int StepOrder { get; set; }

    public string? VariantKey { get; set; }

    public OutreachChannelType Channel { get; set; }

    public string? TemplateName { get; set; }

    public int TrafficPercent { get; set; }

    public int ReachedCount { get; set; }
}
