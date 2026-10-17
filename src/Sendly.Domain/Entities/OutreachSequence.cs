using System.ComponentModel.DataAnnotations;

namespace Sendly.Domain.Entities;

public class OutreachSequence
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public OutreachProject? Project { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<OutreachSequenceStep> Steps { get; set; } = new List<OutreachSequenceStep>();

    public ICollection<ProspectSequenceEnrollment> Enrollments { get; set; } = new List<ProspectSequenceEnrollment>();
}
