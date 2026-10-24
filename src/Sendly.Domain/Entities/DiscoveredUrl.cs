using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

[Index(nameof(ResearchRunId), nameof(NormalizedUrl), IsUnique = true)]
[Index(nameof(ResearchRunId), nameof(Status))]
[Index(nameof(NormalizedDomain))]
public class DiscoveredUrl
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ResearchRunId { get; set; }

    public ResearchRun? ResearchRun { get; set; }

    public Guid ProjectId { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string NormalizedUrl { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? NormalizedDomain { get; set; }

    public DiscoverySourceType SourceType { get; set; }

    [MaxLength(200)]
    public string? SourceName { get; set; }

    public Guid? ParentDiscoveredUrlId { get; set; }

    public DiscoveredUrl? ParentDiscoveredUrl { get; set; }

    public Guid? DiscoverySourceId { get; set; }

    public DiscoverySource? DiscoverySource { get; set; }

    public DiscoveredUrlStatus Status { get; set; } = DiscoveredUrlStatus.Pending;

    public LiveVerificationStatus LiveVerificationStatus { get; set; } = LiveVerificationStatus.NotAttempted;

    public int Depth { get; set; }

    public int RetryCount { get; set; }

    [MaxLength(100)]
    public string? RejectionReasonCode { get; set; }

    [MaxLength(80)]
    public string? PagePurpose { get; set; }

    public int PriorityScore { get; set; }

    [MaxLength(200)]
    public string? DiscoveryReason { get; set; }

    [MaxLength(200)]
    public string? AssociatedIndustry { get; set; }

    [MaxLength(200)]
    public string? AssociatedLocation { get; set; }

    [MaxLength(300)]
    public string? AssociatedOrganizationName { get; set; }

    [MaxLength(200)]
    public string? SchedulingReason { get; set; }

    public int ExpectedDiscoveryValue { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }

    public DateTimeOffset DiscoveredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public DateTimeOffset? LiveVerifiedAtUtc { get; set; }

    public ICollection<CrawledPage> CrawledPages { get; set; } = new List<CrawledPage>();
}
