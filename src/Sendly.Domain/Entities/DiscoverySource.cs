using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

[Index(nameof(NormalizedBaseUrl), IsUnique = true)]
[Index(nameof(Status))]
public class DiscoverySource
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string NormalizedBaseUrl { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? NormalizedDomain { get; set; }

    public DiscoverySourceType SourceType { get; set; }

    [MaxLength(100)]
    public string AdapterKey { get; set; } = "DirectoryListing";

    [MaxLength(2000)]
    public string? CountriesJson { get; set; }

    [MaxLength(2000)]
    public string? IndustriesJson { get; set; }

    public bool AutomatedAccessPermitted { get; set; } = true;

    public DiscoverySourceStatus Status { get; set; } = DiscoverySourceStatus.Active;

    public Guid? ParentDiscoverySourceId { get; set; }

    public DiscoverySource? ParentDiscoverySource { get; set; }

    public DateTimeOffset? LastAttemptedAtUtc { get; set; }

    public DateTimeOffset? LastSucceededAtUtc { get; set; }

    public DateTimeOffset? NextEligibleRetryAtUtc { get; set; }

    public DateTimeOffset? LastFreshnessAssessedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public int CandidatesDiscovered { get; set; }

    public int UniqueCompaniesDiscovered { get; set; }

    public int RelevantCompaniesDiscovered { get; set; }

    public int QualifiedProspectsYielded { get; set; }

    public int PagesAccessible { get; set; }

    public int PagesParsed { get; set; }

    public int LiveVerifiedCompanies { get; set; }

    public int PublicContactsExtracted { get; set; }

    public int DuplicateHits { get; set; }

    public long TotalProcessingMs { get; set; }

    public DateTimeOffset? LastCheckedAtUtc { get; set; }

    public double RelevanceScore { get; set; }

    public double FreshnessScore { get; set; } = 50;

    [MaxLength(1000)]
    public string? LastError { get; set; }

    [MaxLength(2000)]
    public string? ProvenanceNotes { get; set; }

    [MaxLength(4000)]
    public string? ConfigurationJson { get; set; }

    public bool IsSystemManaged { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
