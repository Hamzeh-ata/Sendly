using System.ComponentModel.DataAnnotations;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

public class Campaign
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string HtmlBody { get; set; } = string.Empty;

    public string? PlainTextBody { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    public int TotalRecipients { get; set; }

    public int SuccessCount { get; set; }

    public int FailedCount { get; set; }

    public int CancelledCount { get; set; }

    public int PendingCount { get; set; }

    public int DelayMilliseconds { get; set; }

    public int BatchSize { get; set; }

    public int MaxRetryCount { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? CurrentRecipientEmail { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public ICollection<CampaignRecipient> Recipients { get; set; } = new List<CampaignRecipient>();

    public ICollection<CampaignAttachment> Attachments { get; set; } = new List<CampaignAttachment>();
}
