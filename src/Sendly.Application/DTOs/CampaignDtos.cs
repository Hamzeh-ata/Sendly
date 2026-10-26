using Sendly.Domain.Enums;

namespace Sendly.Application.DTOs;

public class ValidateRecipientsRequestDto
{
    public string RawRecipients { get; set; } = string.Empty;
}

public class ValidateRecipientsResponseDto
{
    public List<string> ValidEmails { get; set; } = [];

    public List<string> InvalidEmails { get; set; } = [];

    public int ValidCount { get; set; }

    public int InvalidCount { get; set; }
}

public class CreateCampaignRecipientDto
{
    public string Email { get; set; } = string.Empty;

    public Guid? ProspectId { get; set; }

    public string? DisplayName { get; set; }

    public string? CompanyName { get; set; }

    public Dictionary<string, string> Personalization { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class CreateCampaignRequestDto
{
    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public string RawRecipients { get; set; } = string.Empty;

    public List<CreateCampaignRecipientDto> PersonalizedRecipients { get; set; } = [];

    public List<Guid> AttachmentIds { get; set; } = [];

    public Guid? SignatureId { get; set; }

    public int? DelayMilliseconds { get; set; }

    public int? BatchSize { get; set; }

    public int? MaxRetryCount { get; set; }

    public string? IdempotencyKey { get; set; }

    public Guid? TemplateId { get; set; }

    public bool AllowAlreadyContacted { get; set; }

    public Guid? EmailAccountId { get; set; }
}

public class TestEmailRequestDto
{
    public string ToEmail { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public List<Guid> AttachmentIds { get; set; } = [];

    public Guid? SignatureId { get; set; }

    public Guid? EmailAccountId { get; set; }
}

public class CampaignSummaryDto
{
    public Guid Id { get; set; }

    public string Subject { get; set; } = string.Empty;

    public CampaignStatus Status { get; set; }

    public int TotalRecipients { get; set; }

    public int SuccessCount { get; set; }

    public int FailedCount { get; set; }

    public int CancelledCount { get; set; }

    public int PendingCount { get; set; }

    public int ProgressPercent { get; set; }

    public string? CurrentRecipientEmail { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public Guid? TemplateId { get; set; }

    public string? TemplateName { get; set; }

    public Guid? EmailAccountId { get; set; }

    public EmailProvider? Provider { get; set; }

    public string? SendingAccountName { get; set; }

    public string? SendingAccountEmail { get; set; }

    public bool AllowAlreadyContacted { get; set; }
}

public class CampaignDetailDto : CampaignSummaryDto
{
    public string HtmlBody { get; set; } = string.Empty;

    public List<CampaignAttachmentDto> Attachments { get; set; } = [];
}

public class SetCampaignAllowAlreadyContactedRequestDto
{
    public bool AllowAlreadyContacted { get; set; }
}

public class CampaignRecipientDto
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public RecipientDeliveryStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    public string? FailureReason { get; set; }
}

public class CampaignAttachmentDto
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}

public class AttachmentUploadResultDto
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}

public class CampaignProgressDto
{
    public Guid CampaignId { get; set; }

    public CampaignStatus Status { get; set; }

    public int TotalRecipients { get; set; }

    public int SuccessCount { get; set; }

    public int FailedCount { get; set; }

    public int CancelledCount { get; set; }

    public int PendingCount { get; set; }

    public int ProgressPercent { get; set; }

    public string? CurrentRecipientEmail { get; set; }

    public CampaignRecipientDto? UpdatedRecipient { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}
