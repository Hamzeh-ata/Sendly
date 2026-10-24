using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Domain.Entities;

[Index(nameof(ProjectId), nameof(Name), IsUnique = true)]
public class ResearchQuery
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public OutreachProject? Project { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public Guid? ActiveVersionId { get; set; }

    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ResearchQueryVersion> Versions { get; set; } = new List<ResearchQueryVersion>();

    public ICollection<ResearchSeedUrl> SeedUrls { get; set; } = new List<ResearchSeedUrl>();
}
