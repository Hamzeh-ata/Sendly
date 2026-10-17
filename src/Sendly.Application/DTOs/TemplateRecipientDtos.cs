using Sendly.Domain.Enums;

namespace Sendly.Application.DTOs;

public class CheckTemplateRecipientsRequestDto
{
    public string RawRecipients { get; set; } = string.Empty;
}

public class TemplateRecipientCheckItemDto
{
    public string Email { get; set; } = string.Empty;

    public bool AlreadyContacted { get; set; }

    public DateTimeOffset? PreviousSentAtUtc { get; set; }

    public Guid? PreviousCampaignId { get; set; }
}

public class CheckTemplateRecipientsResponseDto
{
    public int TotalCount { get; set; }

    public int NewCount { get; set; }

    public int AlreadyContactedCount { get; set; }

    public List<TemplateRecipientCheckItemDto> Recipients { get; set; } = [];
}

public class TemplateRecipientHistoryItemDto
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public TemplateRecipientContactStatus Status { get; set; }

    public DateTimeOffset? SentAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Guid CampaignId { get; set; }

    public string? CampaignSubject { get; set; }

    public string? FailureReason { get; set; }

    public int AttemptCount { get; set; }
}

public class TemplateStatsDto
{
    public int TotalCampaigns { get; set; }

    public int UniqueRecipients { get; set; }

    public int SuccessfullyContacted { get; set; }

    public int Failed { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }
}

public class MarkTemplateRecipientSentRequest
{
    public Guid TemplateId { get; set; }

    public Guid CampaignId { get; set; }

    public string RecipientEmail { get; set; } = string.Empty;

    public EmailProvider? Provider { get; set; }

    public Guid? SendingAccountId { get; set; }

    public string? SendingAccountEmail { get; set; }
}
