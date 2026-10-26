using System.ComponentModel.DataAnnotations;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

public class OutreachProject
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(4000)]
    public string? ResearchInstructions { get; set; }

    public bool IsActive { get; set; } = true;

    public int MinQualificationScore { get; set; } = 70;

    public DuplicateHandlingMode DuplicateHandling { get; set; } = DuplicateHandlingMode.Exclude;

    public bool EnableEmail { get; set; } = true;

    public bool EnableWhatsApp { get; set; }

    public int DefaultRequestedProspectCount { get; set; } = 50;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<OutreachProjectKeyword> Keywords { get; set; } = new List<OutreachProjectKeyword>();

    public ICollection<OutreachProjectCountry> Countries { get; set; } = new List<OutreachProjectCountry>();

    public ICollection<OutreachProjectCity> Cities { get; set; } = new List<OutreachProjectCity>();

    public ICollection<OutreachProjectIndustry> Industries { get; set; } = new List<OutreachProjectIndustry>();

    public ICollection<OutreachProjectTargetRole> TargetRoles { get; set; } = new List<OutreachProjectTargetRole>();

    public ICollection<ProjectTemplate> Templates { get; set; } = new List<ProjectTemplate>();

    public ICollection<Prospect> Prospects { get; set; } = new List<Prospect>();

    public ICollection<ResearchRun> ResearchRuns { get; set; } = new List<ResearchRun>();

    public ICollection<ResearchQuery> ResearchQueries { get; set; } = new List<ResearchQuery>();

    public ICollection<ResearchSeedUrl> SeedUrls { get; set; } = new List<ResearchSeedUrl>();
}
