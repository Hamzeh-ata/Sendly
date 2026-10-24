using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Domain.Entities;

[Index(nameof(ResearchRunId), nameof(NormalizedUrl), IsUnique = true)]
[Index(nameof(ContentHash))]
[Index(nameof(NormalizedDomain))]
public class CrawledPage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ResearchRunId { get; set; }

    public ResearchRun? ResearchRun { get; set; }

    public Guid? DiscoveredUrlId { get; set; }

    public DiscoveredUrl? DiscoveredUrl { get; set; }

    public Guid ProjectId { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string NormalizedUrl { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? NormalizedDomain { get; set; }

    public int? HttpStatusCode { get; set; }

    [MaxLength(200)]
    public string? ContentType { get; set; }

    public long? ContentLengthBytes { get; set; }

    [MaxLength(128)]
    public string? ContentHash { get; set; }

    [MaxLength(500)]
    public string? CanonicalUrl { get; set; }

    [MaxLength(500)]
    public string? FinalUrl { get; set; }

    public bool RobotsAllowed { get; set; } = true;

    [MaxLength(8000)]
    public string? TextExcerpt { get; set; }

    [MaxLength(2000)]
    public string? Title { get; set; }

    [MaxLength(8000)]
    public string? ExternalLinksJson { get; set; }

    [MaxLength(64)]
    public string? PagePurpose { get; set; }

    [MaxLength(1000)]
    public string? Error { get; set; }

    public int CrawlDepth { get; set; }

    public int? DurationMs { get; set; }

    public int RedirectCount { get; set; }

    public bool WasRedirected { get; set; }

    public bool BlockedBySsrf { get; set; }

    public DateTimeOffset CrawledAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
