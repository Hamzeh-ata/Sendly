using System.ComponentModel.DataAnnotations;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

public class CampaignRecipient
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    [Required]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    public RecipientDeliveryStatus Status { get; set; } = RecipientDeliveryStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    [MaxLength(1000)]
    public string? FailureReason { get; set; }

    public bool IsTransientFailure { get; set; }
}
