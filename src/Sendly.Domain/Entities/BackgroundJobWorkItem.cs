using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Domain.Entities;

[Index(nameof(QueueName), nameof(CompletedAtUtc), nameof(AvailableAtUtc))]
[Index(nameof(QueueName), nameof(PayloadId))]
public class BackgroundJobWorkItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string QueueName { get; set; } = string.Empty;

    public Guid PayloadId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset AvailableAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LockedUntilUtc { get; set; }

    [MaxLength(100)]
    public string? LockedBy { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}
