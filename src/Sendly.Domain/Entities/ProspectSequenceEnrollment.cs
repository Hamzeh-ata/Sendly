using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

[Index(nameof(Status), nameof(NextRunAtUtc))]
[Index(nameof(ProspectId), nameof(SequenceId), IsUnique = true)]
public class ProspectSequenceEnrollment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SequenceId { get; set; }

    public OutreachSequence? Sequence { get; set; }

    public Guid ProspectId { get; set; }

    public Prospect? Prospect { get; set; }

    public Guid ProjectId { get; set; }

    public SequenceEnrollmentStatus Status { get; set; } = SequenceEnrollmentStatus.Pending;

    public int CurrentStepOrder { get; set; }

    [MaxLength(20)]
    public string? AssignedVariantKey { get; set; }

    public DateTimeOffset? NextRunAtUtc { get; set; }

    public DateTimeOffset EnrolledAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }
}
