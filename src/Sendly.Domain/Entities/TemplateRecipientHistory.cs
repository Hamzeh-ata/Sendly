using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

[Index(nameof(TemplateId), nameof(NormalizedRecipientEmail), IsUnique = true)]
public class TemplateRecipientHistory
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TemplateId { get; set; }

    public EmailTemplate? Template { get; set; }

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    [Required]
    [MaxLength(320)]
    public string RecipientEmail { get; set; } = string.Empty;

    [Required]
    [MaxLength(320)]
    public string NormalizedRecipientEmail { get; set; } = string.Empty;

    public TemplateRecipientContactStatus Status { get; set; }

    public DateTimeOffset? SentAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? FailureReason { get; set; }

    public int AttemptCount { get; set; }

    public EmailProvider? Provider { get; set; }

    public Guid? SendingAccountId { get; set; }

    [MaxLength(320)]
    public string? SendingAccountEmail { get; set; }
}
