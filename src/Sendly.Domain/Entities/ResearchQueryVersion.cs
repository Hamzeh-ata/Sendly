using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Domain.Entities;

[Index(nameof(ResearchQueryId), nameof(VersionNumber), IsUnique = true)]
public class ResearchQueryVersion
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ResearchQueryId { get; set; }

    public ResearchQuery? ResearchQuery { get; set; }

    public int VersionNumber { get; set; } = 1;

    [MaxLength(8000)]
    public string? PlainLanguageBrief { get; set; }

    [MaxLength(200)]
    public string? TargetIndustry { get; set; }

    [MaxLength(2000)]
    public string? BusinessCategoriesJson { get; set; }

    [MaxLength(100)]
    public string? TargetCountry { get; set; }

    [MaxLength(2000)]
    public string? RegionsJson { get; set; }

    [MaxLength(2000)]
    public string? CitiesJson { get; set; }

    [MaxLength(2000)]
    public string? CompanyTypesJson { get; set; }

    [MaxLength(2000)]
    public string? CompanySizesJson { get; set; }

    [MaxLength(4000)]
    public string? RequiredCriteriaJson { get; set; }

    [MaxLength(4000)]
    public string? ExclusionCriteriaJson { get; set; }

    [MaxLength(2000)]
    public string? RequiredContactInfoJson { get; set; }

    [MaxLength(2000)]
    public string? RequiredEvidenceJson { get; set; }

    [MaxLength(2000)]
    public string? DiscoveryStrategiesJson { get; set; }

    public int MaxPagesPerDomain { get; set; } = 5;

    public int MaxCrawlDepth { get; set; } = 2;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int MaxConcurrentRequests { get; set; } = 2;

    public int RequestsPerDomainPerMinute { get; set; } = 10;

    public int MaxUrlsPerRun { get; set; } = 200;

    public bool RequireOfficialWebsite { get; set; } = true;

    public bool RequirePublicBusinessEmail { get; set; } = true;

    public bool EnableDirectoryDiscovery { get; set; } = true;

    public bool EnableSitemapDiscovery { get; set; } = true;

    public bool EnableLinkExpansion { get; set; } = true;

    public bool EnablePriorResearchUrls { get; set; } = true;

    [MaxLength(16000)]
    public string? StructuredDefinitionJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
