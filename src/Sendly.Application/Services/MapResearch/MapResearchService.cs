using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Application.Services.MapResearch;

public class MapResearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ISendlyDbContext _db;
    private readonly IResearchQueue _queue;
    private readonly MapResearchPlanBuilder _planBuilder;
    private readonly MapContactDiagnosticsService _contactDiagnostics;
    private readonly MapContactEnrichmentService _contactEnrichment;
    private readonly MapOpenAiContactExtractionService _openAiContactExtraction;
    private readonly ResearchOptions _options;
    private readonly ResearchCancellationRegistry _cancellationRegistry;
    private readonly ILogger<MapResearchService> _logger;

    public MapResearchService(
        ISendlyDbContext db,
        IResearchQueue queue,
        MapResearchPlanBuilder planBuilder,
        MapContactDiagnosticsService contactDiagnostics,
        MapContactEnrichmentService contactEnrichment,
        MapOpenAiContactExtractionService openAiContactExtraction,
        IOptions<ResearchOptions> options,
        ResearchCancellationRegistry cancellationRegistry,
        ILogger<MapResearchService> logger)
    {
        _db = db;
        _queue = queue;
        _planBuilder = planBuilder;
        _contactDiagnostics = contactDiagnostics;
        _contactEnrichment = contactEnrichment;
        _openAiContactExtraction = openAiContactExtraction;
        _options = options.Value;
        _cancellationRegistry = cancellationRegistry;
        _logger = logger;
    }

    public Task<MapContactDiagnosticsDto> DiagnoseContactsAsync(
        Guid projectId,
        MapContactDiagnosticsRequestDto request,
        CancellationToken cancellationToken)
        => _contactDiagnostics.DiagnoseAsync(projectId, request, cancellationToken);

    public Task<MapOpenAiContactExtractResultDto> ExtractContactsWithOpenAiAsync(
        Guid projectId,
        Guid prospectId,
        CancellationToken cancellationToken)
        => _openAiContactExtraction.ExtractForProspectAsync(projectId, prospectId, cancellationToken);

    public Task<MapOpenAiContactBatchResultDto> ExtractMissingContactsWithOpenAiAsync(
        Guid projectId,
        Guid? researchRunId,
        CancellationToken cancellationToken)
        => _openAiContactExtraction.ExtractMissingAsync(projectId, researchRunId, cancellationToken);

    public async Task<MapContactReenrichResultDto> ReenrichContactsAsync(
        Guid projectId,
        Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        var run = await ResolveRunAsync(projectId, researchRunId, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("No map research run was found for this project.");
        }

        var request = new ProjectUnderstandingRequest
        {
            ProjectName = project.Name,
            Description = project.Description,
            ResearchInstructions = project.ResearchInstructions,
            ApprovedIndustries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Countries = project.Countries.Select(x => x.Name).ToList(),
            TargetRoles = project.TargetRoles.Select(x => x.Role).ToList(),
            MinQualificationScore = project.MinQualificationScore,
            TargetProspectCount = project.DefaultRequestedProspectCount,
            QueryRequireOfficialWebsite = false,
            QueryRequirePublicBusinessEmail = false,
            Budgets = new ResearchBudgetSpec
            {
                MaxSearchRequests = Math.Clamp(_options.MaxSearchRequests, 5, 80),
                MaxUrlsPerRun = _options.MaxCandidatesPerRun
            }
        };
        var spec = ProjectUnderstandingEngine.Interpret(request);
        var plan = await _planBuilder.BuildAsync(project, spec, request.Budgets!, cancellationToken);

        var prospects = await _db.Prospects
            .Where(x => x.ProjectId == projectId
                        && x.ResearchRunId == run.Id
                        && x.Latitude != null
                        && x.Longitude != null
                        && x.Status != ProspectStatus.Rejected)
            .ToListAsync(cancellationToken);
        prospects = prospects.OrderByDescending(x => x.UpdatedAtUtc).Take(80).ToList();

        var enriched = 0;
        var withPhoneBefore = prospects.Count(x => !string.IsNullOrWhiteSpace(x.Phone));
        var withEmailBefore = prospects.Count(x => !string.IsNullOrWhiteSpace(x.Email));
        foreach (var prospect in prospects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _contactEnrichment.EnrichAsync(run, project, prospect, plan, cancellationToken);
            enriched++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        var withPhoneAfter = prospects.Count(x => !string.IsNullOrWhiteSpace(x.Phone));
        var withEmailAfter = prospects.Count(x => !string.IsNullOrWhiteSpace(x.Email));
        var withWebsiteAfter = prospects.Count(x => !string.IsNullOrWhiteSpace(x.Website));
        return new MapContactReenrichResultDto
        {
            ResearchRunId = run.Id,
            ProspectsProcessed = enriched,
            PhonesBefore = withPhoneBefore,
            PhonesAfter = withPhoneAfter,
            EmailsBefore = withEmailBefore,
            EmailsAfter = withEmailAfter,
            WebsitesAfter = withWebsiteAfter
        };
    }

    public async Task<ResearchRunDetailDto> StartAsync(
        Guid projectId,
        StartMapResearchRequestDto request,
        CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        if (!project.IsActive)
        {
            throw new InvalidOperationException("Cannot start map research on an inactive project.");
        }

        var queryBinding = await ResolveQueryBindingAsync(projectId, request, cancellationToken);
        var requested = request.RequestedProspectCount > 0
            ? request.RequestedProspectCount
            : project.DefaultRequestedProspectCount;
        requested = Math.Clamp(requested, 1, _options.MaxCandidatesPerRun);
        var minScore = Math.Clamp(request.MinQualificationScore ?? project.MinQualificationScore, 0, 100);

        var run = new ResearchRun
        {
            ProjectId = projectId,
            ResearchQueryId = queryBinding.QueryId,
            ResearchQueryVersionId = queryBinding.VersionId,
            Status = ResearchRunStatus.Pending,
            Mode = ResearchMode.Map,
            RequestedProspectCount = requested,
            IncludeEmail = project.EnableEmail,
            IncludeWhatsApp = project.EnableWhatsApp,
            MinQualificationScore = minScore,
            ProgressPercent = 0
        };

        _db.ResearchRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);
        await _queue.EnqueueAsync(run.Id, cancellationToken);
        _logger.LogInformation("Map research started {ResearchRunId} for project {ProjectId}", run.Id, projectId);
        return ResearchRunMapper.MapDetail(run, project.Name);
    }

    public async Task CancelAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var run = await _db.ResearchRuns.FirstOrDefaultAsync(x => x.Id == researchRunId, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("Research run was not found.");
        }

        if (run.Mode != ResearchMode.Map)
        {
            throw new InvalidOperationException("Research run is not a map research run.");
        }

        if (ResearchRunOutcomeResolver.IsTerminal(run.Status))
        {
            return;
        }

        _cancellationRegistry.Cancel(researchRunId);
        if (run.Status == ResearchRunStatus.Pending)
        {
            run.Status = ResearchRunStatus.Cancelled;
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ResumeAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var run = await _db.ResearchRuns.FirstOrDefaultAsync(x => x.Id == researchRunId, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("Research run was not found.");
        }

        if (run.Mode != ResearchMode.Map)
        {
            throw new InvalidOperationException("Research run is not a map research run.");
        }

        if (run.Status is not (ResearchRunStatus.Partial or ResearchRunStatus.Cancelled or ResearchRunStatus.Failed))
        {
            throw new InvalidOperationException("Only partial, cancelled, or failed map runs can be resumed.");
        }

        var stuckRegions = await _db.MapCoverageRegions
            .Where(x => x.ResearchRunId == researchRunId
                        && (x.Status == MapCoverageRegionStatus.InProgress
                            || x.Status == MapCoverageRegionStatus.Failed))
            .ToListAsync(cancellationToken);
        foreach (var region in stuckRegions)
        {
            region.Status = MapCoverageRegionStatus.Pending;
            region.LastError = null;
            region.CompletedAtUtc = null;
            region.StartedAtUtc = null;
        }

        run.Status = ResearchRunStatus.Pending;
        run.Error = null;
        run.CompletedAtUtc = null;
        await _db.SaveChangesAsync(cancellationToken);
        await _queue.EnqueueAsync(run.Id, cancellationToken);
    }

    public async Task<MapResearchOverviewDto> GetOverviewAsync(
        Guid projectId,
        Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        var run = await ResolveRunAsync(projectId, researchRunId, cancellationToken);
        var markers = await ListMarkersAsync(projectId, null, cancellationToken);
        if (run is null)
        {
            return new MapResearchOverviewDto
            {
                MarkersCount = markers.Count,
                Markers = markers
            };
        }

        var regions = await _db.MapCoverageRegions.AsNoTracking()
            .Where(x => x.ResearchRunId == run.Id)
            .ToListAsync(cancellationToken);
        regions = regions.OrderBy(x => x.CreatedAtUtc).ToList();

        var unsupported = ReadUnsupported(run.MapPlanJson);
        return new MapResearchOverviewDto
        {
            ActiveRunId = run.Id,
            ActiveRunStatus = run.Status,
            ProgressPercent = run.ProgressPercent,
            MarkersCount = markers.Count,
            RegionsTotal = regions.Count,
            RegionsCompleted = regions.Count(x => x.Status == MapCoverageRegionStatus.Completed),
            RegionsPending = regions.Count(x => x.Status == MapCoverageRegionStatus.Pending),
            RegionsFailed = regions.Count(x => x.Status == MapCoverageRegionStatus.Failed),
            PlacesDiscovered = run.MapPlacesDiscovered,
            ProspectsSaved = run.ProspectsSaved,
            CoverageSummaryJson = run.MapCoverageSummaryJson,
            UnsupportedCategories = unsupported,
            Regions = regions.Select(MapRegion).ToList(),
            Markers = markers
        };
    }

    public async Task<List<MapMarkerDto>> ListMarkersAsync(
        Guid projectId,
        Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        var query = _db.Prospects.AsNoTracking()
            .Where(x => x.ProjectId == projectId
                        && x.Latitude != null
                        && x.Longitude != null
                        && x.Status != ProspectStatus.Rejected);
        if (researchRunId is not null)
        {
            query = query.Where(x => x.ResearchRunId == researchRunId.Value);
        }

        var prospects = await query.ToListAsync(cancellationToken);
        prospects = prospects
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(2000)
            .ToList();

        var prospectIds = prospects.Select(x => x.Id).ToList();
        var evidenceItems = await _db.ResearchEvidenceItems.AsNoTracking()
            .Where(x => x.ProspectId != null && prospectIds.Contains(x.ProspectId.Value))
            .ToListAsync(cancellationToken);
        var evidenceByProspect = new Dictionary<Guid, List<ResearchEvidence>>();
        foreach (var item in evidenceItems)
        {
            if (item.ProspectId is null)
            {
                continue;
            }

            if (!evidenceByProspect.TryGetValue(item.ProspectId.Value, out var list))
            {
                list = [];
                evidenceByProspect[item.ProspectId.Value] = list;
            }

            list.Add(item);
        }

        var markers = new List<MapMarkerDto>();
        foreach (var prospect in prospects)
        {
            if (!IsValidCoordinate(prospect.Latitude, prospect.Longitude))
            {
                continue;
            }

            evidenceByProspect.TryGetValue(prospect.Id, out var evidence);
            markers.Add(MapMarker(prospect, evidence ?? []));
        }

        return markers;
    }

    public async Task<MapResearchPlanPreviewDto> PreviewPlanAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        var request = new ProjectUnderstandingRequest
        {
            ProjectName = project.Name,
            Description = project.Description,
            ResearchInstructions = project.ResearchInstructions,
            ApprovedIndustries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Countries = project.Countries.Select(x => x.Name).ToList(),
            TargetRoles = project.TargetRoles.Select(x => x.Role).ToList(),
            MinQualificationScore = project.MinQualificationScore,
            TargetProspectCount = project.DefaultRequestedProspectCount,
            QueryRequireOfficialWebsite = false,
            QueryRequirePublicBusinessEmail = false,
            Budgets = new ResearchBudgetSpec
            {
                MaxSearchRequests = Math.Clamp(_options.MaxSearchRequests, 5, 80),
                MaxUrlsPerRun = _options.MaxCandidatesPerRun
            }
        };
        var spec = ProjectUnderstandingEngine.Interpret(request);
        var plan = await _planBuilder.BuildAsync(project, spec, request.Budgets!, cancellationToken);
        return new MapResearchPlanPreviewDto
        {
            IsValid = plan.IsValid,
            GenerationLog = plan.GenerationLog,
            KeywordFamilies = plan.KeywordFamilies.Select(x => x.BusinessConcept).ToList(),
            SourceCategories = plan.SourceCategoryMappings
                .Where(x => x.IsSupported)
                .Select(x => x.SourceCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UnsupportedCategories = plan.UnsupportedCategories,
            GeographicCellCount = plan.GeographicCells.Count,
            ValidationIssues = plan.ValidationIssues.Select(x => $"{x.Severity}:{x.Code}:{x.Message}").ToList()
        };
    }

    private async Task<ResearchRun?> ResolveRunAsync(
        Guid projectId,
        Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        if (researchRunId is not null)
        {
            return await _db.ResearchRuns.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == researchRunId.Value && x.ProjectId == projectId, cancellationToken);
        }

        var runs = await _db.ResearchRuns.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Mode == ResearchMode.Map)
            .ToListAsync(cancellationToken);
        return runs.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
    }

    private async Task<(Guid? QueryId, Guid? VersionId)> ResolveQueryBindingAsync(
        Guid projectId,
        StartMapResearchRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.ResearchQueryId is not null)
        {
            var query = await _db.ResearchQueries.FirstOrDefaultAsync(
                x => x.Id == request.ResearchQueryId.Value && x.ProjectId == projectId && !x.IsArchived,
                cancellationToken);
            if (query is null)
            {
                throw new InvalidOperationException("Research query was not found.");
            }

            var versionId = request.ResearchQueryVersionId ?? query.ActiveVersionId;
            return (query.Id, versionId);
        }

        var queries = await _db.ResearchQueries.AsNoTracking()
            .Where(x => x.ProjectId == projectId && !x.IsArchived)
            .ToListAsync(cancellationToken);
        var latest = queries.OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
        return (latest?.Id, latest?.ActiveVersionId);
    }

    private async Task<OutreachProject?> LoadProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await _db.OutreachProjects.FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        project.Keywords = await _db.OutreachProjectKeywords.Where(x => x.ProjectId == projectId).ToListAsync(cancellationToken);
        project.Countries = await _db.OutreachProjectCountries.Where(x => x.ProjectId == projectId).ToListAsync(cancellationToken);
        project.Cities = await _db.OutreachProjectCities.Where(x => x.ProjectId == projectId).ToListAsync(cancellationToken);
        project.Industries = await _db.OutreachProjectIndustries.Where(x => x.ProjectId == projectId).ToListAsync(cancellationToken);
        project.TargetRoles = await _db.OutreachProjectTargetRoles.Where(x => x.ProjectId == projectId).ToListAsync(cancellationToken);
        return project;
    }

    private static MapMarkerDto MapMarker(Prospect prospect, IReadOnlyList<ResearchEvidence> evidence)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(prospect.Website))
        {
            missing.Add("website");
        }

        if (string.IsNullOrWhiteSpace(prospect.Email))
        {
            missing.Add("email");
        }

        if (string.IsNullOrWhiteSpace(prospect.Phone))
        {
            missing.Add("phone");
        }

        if (string.IsNullOrWhiteSpace(prospect.StreetAddress))
        {
            missing.Add("streetAddress");
        }

        return new MapMarkerDto
        {
            ProspectId = prospect.Id,
            ResearchRunId = prospect.ResearchRunId,
            CompanyName = prospect.CompanyName,
            Latitude = prospect.Latitude!.Value,
            Longitude = prospect.Longitude!.Value,
            City = prospect.City,
            Country = prospect.Country,
            Industry = prospect.Industry,
            Website = prospect.Website,
            Phone = prospect.Phone,
            Email = prospect.Email,
            Fax = prospect.Fax,
            StreetAddress = prospect.StreetAddress,
            OpeningHours = prospect.OpeningHours,
            SourceName = prospect.SourceName,
            SourceUrl = prospect.SourceUrl,
            SourceObjectKey = prospect.SourceObjectKey,
            CategoryTags = prospect.CategoryTagsJson,
            Status = prospect.Status,
            IsQualified = prospect.IsQualified,
            QualificationScore = prospect.QualificationScore,
            MissingFields = missing,
            AdditionalPhones = ProspectContactStore.Parse(prospect.AdditionalPhonesJson),
            AdditionalEmails = ProspectContactStore.Parse(prospect.AdditionalEmailsJson),
            FieldProvenance = MapProvenance(evidence)
        };
    }

    private static List<MapFieldProvenanceDto> MapProvenance(IReadOnlyList<ResearchEvidence> evidence)
    {
        var result = new List<MapFieldProvenanceDto>();
        foreach (var item in evidence)
        {
            if (!item.FactType.StartsWith("map.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.FactType, "map.rawTags", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var notes = item.Notes ?? string.Empty;
            result.Add(new MapFieldProvenanceDto
            {
                FieldName = item.FactType["map.".Length..],
                Value = item.FactValue,
                SourceUrl = item.SourceUrl,
                SourceTagKey = ExtractNotePart(notes, "tag="),
                ExtractionMethod = ResolveExtractionMethod(notes),
                VerificationStatus = ResolveVerificationStatus(notes),
                SupportingExcerpt = ExtractNotePart(notes, "excerpt="),
                RetrievedAtUtc = item.LastVerifiedAtUtc
            });
        }

        return result;
    }

    private static string ResolveExtractionMethod(string notes)
    {
        if (notes.Contains("ollama_assisted", StringComparison.OrdinalIgnoreCase))
        {
            return "ollama_assisted";
        }

        if (notes.Contains("html_extraction", StringComparison.OrdinalIgnoreCase))
        {
            return "html_extraction";
        }

        if (notes.Contains("structured_metadata", StringComparison.OrdinalIgnoreCase))
        {
            return "structured_metadata";
        }

        if (notes.Contains("web_search", StringComparison.OrdinalIgnoreCase))
        {
            return "web_search";
        }

        return "structured_tag";
    }

    private static string ResolveVerificationStatus(string notes)
    {
        if (notes.Contains("validated_against_source", StringComparison.OrdinalIgnoreCase))
        {
            return "validated_against_source";
        }

        if (notes.Contains("rejected_unsupported_by_source", StringComparison.OrdinalIgnoreCase))
        {
            return "rejected_unsupported_by_source";
        }

        if (notes.Contains("unverified", StringComparison.OrdinalIgnoreCase))
        {
            return "unverified";
        }

        return "source_reported";
    }

    private static string? ExtractNotePart(string notes, string prefix)
    {
        var parts = notes.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return part[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static MapCoverageRegionDto MapRegion(MapCoverageRegion region)
        => new()
        {
            Id = region.Id,
            ResearchRunId = region.ResearchRunId,
            RegionLabel = region.RegionLabel,
            CountryCode = region.CountryCode,
            South = region.South,
            West = region.West,
            North = region.North,
            East = region.East,
            KeywordFamilyKey = region.KeywordFamilyKey,
            SourceName = region.SourceName,
            Status = region.Status,
            RecordsFound = region.RecordsFound,
            UniqueRecordsFound = region.UniqueRecordsFound,
            LastError = region.LastError,
            CoverageNotes = region.CoverageNotes
        };

    private static List<string> ReadUnsupported(string? mapPlanJson)
    {
        if (string.IsNullOrWhiteSpace(mapPlanJson))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(mapPlanJson);
            if (!doc.RootElement.TryGetProperty("unsupportedCategories", out var unsupported)
                || unsupported.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<string>();
            foreach (var item in unsupported.EnumerateArray())
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value);
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsValidCoordinate(double? latitude, double? longitude)
        => latitude is >= -90 and <= 90
           && longitude is >= -180 and <= 180
           && !(Math.Abs(latitude.Value) < 0.000001 && Math.Abs(longitude.Value) < 0.000001);
}
