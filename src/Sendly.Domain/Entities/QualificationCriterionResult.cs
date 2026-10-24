using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Domain.Entities;

[Index(nameof(ProspectId), nameof(CriterionKey), IsUnique = true)]
[Index(nameof(ResearchRunId))]
public class QualificationCriterionResult
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public Guid ResearchRunId { get; set; }

    public ResearchRun? ResearchRun { get; set; }

    public Guid ProspectId { get; set; }

    public Prospect? Prospect { get; set; }

    [Required]
    [MaxLength(100)]
    public string CriterionKey { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? CriterionLabel { get; set; }

    public bool IsMandatory { get; set; }

    public bool Passed { get; set; }

    public int ScoreContribution { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }

    [MaxLength(4000)]
    public string? EvidenceIdsJson { get; set; }

    public DateTimeOffset EvaluatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
