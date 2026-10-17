using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

[Index(nameof(NormalizedValue), nameof(Channel), IsUnique = true)]
public class OutreachContactHistory
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public OutreachChannelType Channel { get; set; }

    [Required]
    [MaxLength(320)]
    public string ContactValue { get; set; } = string.Empty;

    [Required]
    [MaxLength(320)]
    public string NormalizedValue { get; set; } = string.Empty;

    public Guid? ProspectId { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid? TemplateId { get; set; }

    public DateTimeOffset ContactedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
