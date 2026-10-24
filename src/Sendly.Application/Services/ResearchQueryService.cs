using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class ResearchQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ISendlyDbContext _db;

    public ResearchQueryService(ISendlyDbContext db)
    {
        _db = db;
    }

    public async Task<List<ResearchQuerySummaryDto>> ListByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await EnsureProjectExistsAsync(projectId, cancellationToken);
        var queries = await _db.ResearchQueries
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && !x.IsArchived)
            .ToListAsync(cancellationToken);
        queries = queries.OrderByDescending(x => x.UpdatedAtUtc).ToList();

        var result = new List<ResearchQuerySummaryDto>();
        foreach (var query in queries)
        {
            result.Add(await MapSummaryAsync(query, cancellationToken));
        }

        return result;
    }

    public async Task<ResearchQueryDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var query = await _db.ResearchQueries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (query is null)
        {
            return null;
        }

        return await MapDetailAsync(query, cancellationToken);
    }

    public async Task<ResearchQueryDetailDto> CreateAsync(
        Guid projectId,
        UpsertResearchQueryDto request,
        CancellationToken cancellationToken)
    {
        await EnsureProjectExistsAsync(projectId, cancellationToken);
        ValidateRequest(request);

        var name = request.Name.Trim();
        await EnsureUniqueNameAsync(projectId, name, null, cancellationToken);

        var query = new ResearchQuery
        {
            ProjectId = projectId,
            Name = name,
            Description = TrimOrNull(request.Description),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        var version = BuildVersion(query.Id, 1, request);
        query.ActiveVersionId = version.Id;

        _db.ResearchQueries.Add(query);
        _db.ResearchQueryVersions.Add(version);
        ReplaceSeedUrls(query, request.SeedUrls);
        await _db.SaveChangesAsync(cancellationToken);

        return (await GetAsync(query.Id, cancellationToken))!;
    }

    public async Task<ResearchQueryDetailDto> UpdateAsync(
        Guid id,
        UpsertResearchQueryDto request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var query = await _db.ResearchQueries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (query is null)
        {
            throw new InvalidOperationException("Research query was not found.");
        }

        if (query.IsArchived)
        {
            throw new InvalidOperationException("Cannot update an archived research query.");
        }

        var name = request.Name.Trim();
        await EnsureUniqueNameAsync(query.ProjectId, name, query.Id, cancellationToken);

        query.Name = name;
        query.Description = TrimOrNull(request.Description);
        query.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var nextVersionNumber = await ResolveNextVersionNumberAsync(query.Id, cancellationToken);
        var version = BuildVersion(query.Id, nextVersionNumber, request);
        query.ActiveVersionId = version.Id;
        _db.ResearchQueryVersions.Add(version);

        var existingSeeds = await _db.ResearchSeedUrls
            .Where(x => x.ResearchQueryId == query.Id)
            .ToListAsync(cancellationToken);
        foreach (var seed in existingSeeds)
        {
            _db.ResearchSeedUrls.Remove(seed);
        }

        ReplaceSeedUrls(query, request.SeedUrls);
        await _db.SaveChangesAsync(cancellationToken);

        return (await GetAsync(query.Id, cancellationToken))!;
    }

    public async Task<ResearchQueryDetailDto> DuplicateAsync(
        Guid id,
        DuplicateResearchQueryDto request,
        CancellationToken cancellationToken)
    {
        var source = await GetAsync(id, cancellationToken);
        if (source is null || source.ActiveVersion is null)
        {
            throw new InvalidOperationException("Research query was not found.");
        }

        var name = string.IsNullOrWhiteSpace(request.Name)
            ? $"{source.Name} (copy)"
            : request.Name.Trim();

        var upsert = MapToUpsert(source);
        upsert.Name = name;
        return await CreateAsync(source.ProjectId, upsert, cancellationToken);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var query = await _db.ResearchQueries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (query is null)
        {
            throw new InvalidOperationException("Research query was not found.");
        }

        query.IsArchived = true;
        query.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var query = await _db.ResearchQueries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (query is null)
        {
            throw new InvalidOperationException("Research query was not found.");
        }

        var linkedRuns = await _db.ResearchRuns.CountAsync(x => x.ResearchQueryId == id, cancellationToken);
        if (linkedRuns > 0)
        {
            throw new InvalidOperationException("Cannot delete a research query that has research runs. Archive it instead.");
        }

        var versions = await _db.ResearchQueryVersions
            .Where(x => x.ResearchQueryId == id)
            .ToListAsync(cancellationToken);
        var seeds = await _db.ResearchSeedUrls
            .Where(x => x.ResearchQueryId == id)
            .ToListAsync(cancellationToken);

        foreach (var seed in seeds)
        {
            _db.ResearchSeedUrls.Remove(seed);
        }

        foreach (var version in versions)
        {
            _db.ResearchQueryVersions.Remove(version);
        }

        _db.ResearchQueries.Remove(query);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureProjectExistsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var exists = await _db.OutreachProjects.AnyAsync(x => x.Id == projectId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Project was not found.");
        }
    }

    private async Task EnsureUniqueNameAsync(
        Guid projectId,
        string name,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var query = _db.ResearchQueries.Where(x =>
            x.ProjectId == projectId
            && !x.IsArchived
            && x.Name == name);
        if (excludeId is not null)
        {
            query = query.Where(x => x.Id != excludeId.Value);
        }

        if (await query.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("A research query with this name already exists on the project.");
        }
    }

    private static void ValidateRequest(UpsertResearchQueryDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Query name is required.");
        }

        request.MaxPagesPerDomain = Math.Clamp(request.MaxPagesPerDomain, 1, 50);
        request.MaxCrawlDepth = Math.Clamp(request.MaxCrawlDepth, 0, 8);
        request.RequestTimeoutSeconds = Math.Clamp(request.RequestTimeoutSeconds, 5, 120);
        request.MaxConcurrentRequests = Math.Clamp(request.MaxConcurrentRequests, 1, 10);
        request.RequestsPerDomainPerMinute = Math.Clamp(request.RequestsPerDomainPerMinute, 1, 60);
        request.MaxUrlsPerRun = Math.Clamp(request.MaxUrlsPerRun, 1, 2000);
    }

    private ResearchQueryVersion BuildVersion(
        Guid queryId,
        int versionNumber,
        UpsertResearchQueryDto request)
    {
        return new ResearchQueryVersion
        {
            Id = Guid.NewGuid(),
            ResearchQueryId = queryId,
            VersionNumber = versionNumber,
            PlainLanguageBrief = TrimOrNull(request.PlainLanguageBrief),
            TargetIndustry = TrimOrNull(request.TargetIndustry),
            BusinessCategoriesJson = SerializeList(request.BusinessCategories),
            TargetCountry = TrimOrNull(request.TargetCountry),
            RegionsJson = SerializeList(request.Regions),
            CitiesJson = SerializeList(request.Cities),
            CompanyTypesJson = SerializeList(request.CompanyTypes),
            CompanySizesJson = SerializeList(request.CompanySizes),
            RequiredCriteriaJson = SerializeList(request.RequiredCriteria),
            ExclusionCriteriaJson = SerializeList(request.ExclusionCriteria),
            RequiredContactInfoJson = SerializeList(request.RequiredContactInfo),
            RequiredEvidenceJson = SerializeList(request.RequiredEvidence),
            DiscoveryStrategiesJson = SerializeList(request.DiscoveryStrategies),
            MaxPagesPerDomain = request.MaxPagesPerDomain,
            MaxCrawlDepth = request.MaxCrawlDepth,
            RequestTimeoutSeconds = request.RequestTimeoutSeconds,
            MaxConcurrentRequests = request.MaxConcurrentRequests,
            RequestsPerDomainPerMinute = request.RequestsPerDomainPerMinute,
            MaxUrlsPerRun = request.MaxUrlsPerRun,
            RequireOfficialWebsite = request.RequireOfficialWebsite,
            RequirePublicBusinessEmail = request.RequirePublicBusinessEmail,
            EnableDirectoryDiscovery = request.EnableDirectoryDiscovery,
            EnableSitemapDiscovery = request.EnableSitemapDiscovery,
            EnableLinkExpansion = request.EnableLinkExpansion,
            EnablePriorResearchUrls = request.EnablePriorResearchUrls,
            StructuredDefinitionJson = JsonSerializer.Serialize(request, JsonOptions),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private void ReplaceSeedUrls(ResearchQuery query, List<UpsertResearchSeedUrlDto> seeds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            var normalized = ProspectNormalizer.NormalizeUrl(seed.Url);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                continue;
            }

            if (!TryBuildAbsoluteUrl(seed.Url, out var absoluteUrl))
            {
                continue;
            }

            _db.ResearchSeedUrls.Add(new ResearchSeedUrl
            {
                ProjectId = query.ProjectId,
                ResearchQueryId = query.Id,
                Url = absoluteUrl,
                NormalizedUrl = normalized,
                NormalizedDomain = ProspectNormalizer.NormalizeWebsite(absoluteUrl),
                Label = TrimOrNull(seed.Label),
                IsActive = seed.IsActive,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private async Task<int> ResolveNextVersionNumberAsync(Guid queryId, CancellationToken cancellationToken)
    {
        var max = await _db.ResearchQueryVersions
            .Where(x => x.ResearchQueryId == queryId)
            .Select(x => (int?)x.VersionNumber)
            .MaxAsync(cancellationToken);
        return (max ?? 0) + 1;
    }

    private async Task<ResearchQuerySummaryDto> MapSummaryAsync(
        ResearchQuery query,
        CancellationToken cancellationToken)
    {
        var versionCount = await _db.ResearchQueryVersions
            .AsNoTracking()
            .CountAsync(x => x.ResearchQueryId == query.Id, cancellationToken);
        var seedCount = await _db.ResearchSeedUrls
            .AsNoTracking()
            .CountAsync(x => x.ResearchQueryId == query.Id && x.IsActive, cancellationToken);

        return new ResearchQuerySummaryDto
        {
            Id = query.Id,
            ProjectId = query.ProjectId,
            Name = query.Name,
            Description = query.Description,
            ActiveVersionId = query.ActiveVersionId,
            VersionCount = versionCount,
            ActiveSeedUrlCount = seedCount,
            IsArchived = query.IsArchived,
            CreatedAtUtc = query.CreatedAtUtc,
            UpdatedAtUtc = query.UpdatedAtUtc
        };
    }

    private async Task<ResearchQueryDetailDto> MapDetailAsync(
        ResearchQuery query,
        CancellationToken cancellationToken)
    {
        var summary = await MapSummaryAsync(query, cancellationToken);
        var versions = await _db.ResearchQueryVersions
            .AsNoTracking()
            .Where(x => x.ResearchQueryId == query.Id)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync(cancellationToken);
        var seeds = await _db.ResearchSeedUrls
            .AsNoTracking()
            .Where(x => x.ResearchQueryId == query.Id)
            .ToListAsync(cancellationToken);
        seeds = seeds.OrderBy(x => x.CreatedAtUtc).ToList();

        var detail = new ResearchQueryDetailDto
        {
            Id = summary.Id,
            ProjectId = summary.ProjectId,
            Name = summary.Name,
            Description = summary.Description,
            ActiveVersionId = summary.ActiveVersionId,
            VersionCount = summary.VersionCount,
            ActiveSeedUrlCount = summary.ActiveSeedUrlCount,
            IsArchived = summary.IsArchived,
            CreatedAtUtc = summary.CreatedAtUtc,
            UpdatedAtUtc = summary.UpdatedAtUtc,
            Versions = versions.Select(MapVersion).ToList(),
            SeedUrls = seeds.Select(MapSeed).ToList()
        };

        detail.ActiveVersion = detail.Versions.FirstOrDefault(x => x.Id == query.ActiveVersionId)
            ?? detail.Versions.FirstOrDefault();
        return detail;
    }

    private static ResearchQueryVersionDto MapVersion(ResearchQueryVersion version)
    {
        return new ResearchQueryVersionDto
        {
            Id = version.Id,
            VersionNumber = version.VersionNumber,
            PlainLanguageBrief = version.PlainLanguageBrief,
            TargetIndustry = version.TargetIndustry,
            BusinessCategories = DeserializeList(version.BusinessCategoriesJson),
            TargetCountry = version.TargetCountry,
            Regions = DeserializeList(version.RegionsJson),
            Cities = DeserializeList(version.CitiesJson),
            CompanyTypes = DeserializeList(version.CompanyTypesJson),
            CompanySizes = DeserializeList(version.CompanySizesJson),
            RequiredCriteria = DeserializeList(version.RequiredCriteriaJson),
            ExclusionCriteria = DeserializeList(version.ExclusionCriteriaJson),
            RequiredContactInfo = DeserializeList(version.RequiredContactInfoJson),
            RequiredEvidence = DeserializeList(version.RequiredEvidenceJson),
            DiscoveryStrategies = DeserializeList(version.DiscoveryStrategiesJson),
            MaxPagesPerDomain = version.MaxPagesPerDomain,
            MaxCrawlDepth = version.MaxCrawlDepth,
            RequestTimeoutSeconds = version.RequestTimeoutSeconds,
            MaxConcurrentRequests = version.MaxConcurrentRequests,
            RequestsPerDomainPerMinute = version.RequestsPerDomainPerMinute,
            MaxUrlsPerRun = version.MaxUrlsPerRun,
            RequireOfficialWebsite = version.RequireOfficialWebsite,
            RequirePublicBusinessEmail = version.RequirePublicBusinessEmail,
            EnableDirectoryDiscovery = version.EnableDirectoryDiscovery,
            EnableSitemapDiscovery = version.EnableSitemapDiscovery,
            EnableLinkExpansion = version.EnableLinkExpansion,
            EnablePriorResearchUrls = version.EnablePriorResearchUrls,
            CreatedAtUtc = version.CreatedAtUtc
        };
    }

    private static ResearchSeedUrlDto MapSeed(ResearchSeedUrl seed)
    {
        return new ResearchSeedUrlDto
        {
            Id = seed.Id,
            Url = seed.Url,
            NormalizedUrl = seed.NormalizedUrl,
            NormalizedDomain = seed.NormalizedDomain,
            Label = seed.Label,
            IsActive = seed.IsActive,
            CreatedAtUtc = seed.CreatedAtUtc
        };
    }

    private static UpsertResearchQueryDto MapToUpsert(ResearchQueryDetailDto source)
    {
        var version = source.ActiveVersion!;
        return new UpsertResearchQueryDto
        {
            Name = source.Name,
            Description = source.Description,
            PlainLanguageBrief = version.PlainLanguageBrief,
            TargetIndustry = version.TargetIndustry,
            BusinessCategories = version.BusinessCategories,
            TargetCountry = version.TargetCountry,
            Regions = version.Regions,
            Cities = version.Cities,
            CompanyTypes = version.CompanyTypes,
            CompanySizes = version.CompanySizes,
            RequiredCriteria = version.RequiredCriteria,
            ExclusionCriteria = version.ExclusionCriteria,
            RequiredContactInfo = version.RequiredContactInfo,
            RequiredEvidence = version.RequiredEvidence,
            DiscoveryStrategies = version.DiscoveryStrategies,
            MaxPagesPerDomain = version.MaxPagesPerDomain,
            MaxCrawlDepth = version.MaxCrawlDepth,
            RequestTimeoutSeconds = version.RequestTimeoutSeconds,
            MaxConcurrentRequests = version.MaxConcurrentRequests,
            RequestsPerDomainPerMinute = version.RequestsPerDomainPerMinute,
            MaxUrlsPerRun = version.MaxUrlsPerRun,
            RequireOfficialWebsite = version.RequireOfficialWebsite,
            RequirePublicBusinessEmail = version.RequirePublicBusinessEmail,
            EnableDirectoryDiscovery = version.EnableDirectoryDiscovery,
            EnableSitemapDiscovery = version.EnableSitemapDiscovery,
            EnableLinkExpansion = version.EnableLinkExpansion,
            EnablePriorResearchUrls = version.EnablePriorResearchUrls,
            SeedUrls = source.SeedUrls.Select(x => new UpsertResearchSeedUrlDto
            {
                Url = x.Url,
                Label = x.Label,
                IsActive = x.IsActive
            }).ToList()
        };
    }

    private static string? SerializeList(List<string> values)
    {
        var cleaned = NormalizeStringList(values);
        return cleaned.Count == 0 ? null : JsonSerializer.Serialize(cleaned, JsonOptions);
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> NormalizeStringList(List<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private static bool TryBuildAbsoluteUrl(string? value, out string absoluteUrl)
    {
        absoluteUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var raw = value.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            raw = "https://" + raw;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        absoluteUrl = uri.ToString();
        return true;
    }

    private static string? TrimOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
