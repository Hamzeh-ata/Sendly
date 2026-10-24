using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Domain.Entities;

[Index(nameof(ResearchQueryId), nameof(NormalizedUrl), IsUnique = true)]
[Index(nameof(ProjectId), nameof(NormalizedUrl))]
public class ResearchSeedUrl
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public OutreachProject? Project { get; set; }

    public Guid ResearchQueryId { get; set; }

    public ResearchQuery? ResearchQuery { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string NormalizedUrl { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? NormalizedDomain { get; set; }

    [MaxLength(500)]
    public string? Label { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
