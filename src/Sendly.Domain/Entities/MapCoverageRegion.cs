using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

[Index(nameof(ResearchRunId), nameof(Status))]
[Index(nameof(ProjectId), nameof(CountryCode))]
[Index(nameof(ResearchRunId), nameof(SourceName), nameof(KeywordFamilyKey))]
public class MapCoverageRegion
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public Guid ResearchRunId { get; set; }

    public ResearchRun? ResearchRun { get; set; }

    public Guid? ParentRegionId { get; set; }

    [MaxLength(8)]
    public string? CountryCode { get; set; }

    [MaxLength(200)]
    public string RegionLabel { get; set; } = string.Empty;

    public double South { get; set; }

    public double West { get; set; }

    public double North { get; set; }

    public double East { get; set; }

    [MaxLength(100)]
    public string SourceName { get; set; } = "OpenStreetMapOverpass";

    [MaxLength(200)]
    public string KeywordFamilyKey { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? SourceCategoriesJson { get; set; }

    public MapCoverageRegionStatus Status { get; set; } = MapCoverageRegionStatus.Pending;

    public int AttemptCount { get; set; }

    public int RecordsFound { get; set; }

    public int UniqueRecordsFound { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }

    [MaxLength(500)]
    public string? CoverageNotes { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}
