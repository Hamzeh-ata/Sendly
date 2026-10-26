using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Application.Services.MapResearch;

public class MapResearchProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ISendlyDbContext _db;
    private readonly IMapGeographicSource _mapSource;
    private readonly MapResearchPlanBuilder _planBuilder;
    private readonly ProspectQualificationService _qualificationService;
    private readonly MapContactEnrichmentService _contactEnrichment;
    private readonly ResearchCancellationRegistry _cancellationRegistry;
    private readonly IResearchProgressNotifier _progressNotifier;
    private readonly IResearchEventRecorder _events;
    private readonly ResearchOptions _options;
    private readonly ILogger<MapResearchProcessor> _logger;

    public MapResearchProcessor(
        ISendlyDbContext db,
        IMapGeographicSource mapSource,
        MapResearchPlanBuilder planBuilder,
        ProspectQualificationService qualificationService,
        MapContactEnrichmentService contactEnrichment,
        ResearchCancellationRegistry cancellationRegistry,
        IResearchProgressNotifier progressNotifier,
        IResearchEventRecorder events,
        IOptions<ResearchOptions> options,
        ILogger<MapResearchProcessor> logger)
    {
        _db = db;
        _mapSource = mapSource;
        _planBuilder = planBuilder;
        _qualificationService = qualificationService;
        _contactEnrichment = contactEnrichment;
        _cancellationRegistry = cancellationRegistry;
        _progressNotifier = progressNotifier;
        _events = events;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid researchRunId, CancellationToken stoppingToken)
    {
        var runToken = _cancellationRegistry.GetOrCreate(researchRunId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, runToken);
        var cancellationToken = linked.Token;

        var run = await _db.ResearchRuns.FirstOrDefaultAsync(x => x.Id == researchRunId, cancellationToken);
        if (run is null || ResearchRunOutcomeResolver.IsTerminal(run.Status))
        {
            _cancellationRegistry.Clear(researchRunId);
            return;
        }

        var project = await LoadProjectAsync(run.ProjectId, cancellationToken);
        if (project is null)
        {
            await FailRunAsync(run, "Project was not found.", cancellationToken);
            return;
        }

        run.Status = ResearchRunStatus.Running;
        run.StartedAtUtc ??= DateTimeOffset.UtcNow;
        run.Mode = ResearchMode.Map;
        await _db.SaveChangesAsync(cancellationToken);
        await RecordEventAsync(run.Id, ResearchRunEventType.Started, ResearchRunEventSeverity.Info,
            "Map research started.", null, cancellationToken);
        await NotifyAsync(run, project.Name, cancellationToken);

        try
        {
            var spec = await ResolveSpecificationAsync(run, project, cancellationToken);
            var budgets = BuildBudgets(run, spec);
            var plan = await LoadOrBuildPlanAsync(run, project, spec, budgets, cancellationToken);
            if (!plan.IsValid)
            {
                run.Status = ResearchRunStatus.Failed;
                run.Error = string.Join("; ", plan.ValidationIssues
                    .Where(x => string.Equals(x.Severity, "error", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Message));
                run.CompletedAtUtc = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                await RecordEventAsync(run.Id, ResearchRunEventType.Error, ResearchRunEventSeverity.Error,
                    run.Error ?? "Map plan invalid.", null, cancellationToken);
                await NotifyAsync(run, project.Name, cancellationToken);
                _cancellationRegistry.Clear(researchRunId);
                return;
            }

            await EnsureCoverageRegionsAsync(run, plan, cancellationToken);
            await ExecuteCoverageAsync(run, project, plan, cancellationToken);
            await FinalizeRunAsync(run, project.Name, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Status = ResearchRunStatus.Cancelled;
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await RecordEventAsync(run.Id, ResearchRunEventType.Cancelled, ResearchRunEventSeverity.Warning,
                "Map research cancelled.", null, CancellationToken.None);
            await NotifyAsync(run, project.Name, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Map research failed for {ResearchRunId}", researchRunId);
            run.Status = ResearchRunStatus.Failed;
            run.Error = Trim(ex.Message, 2000);
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await RecordEventAsync(run.Id, ResearchRunEventType.Error, ResearchRunEventSeverity.Error,
                run.Error, null, CancellationToken.None);
            await NotifyAsync(run, project.Name, CancellationToken.None);
        }
        finally
        {
            _cancellationRegistry.Clear(researchRunId);
        }
    }

    private async Task ExecuteCoverageAsync(
        ResearchRun run,
        OutreachProject project,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingProspects = await _db.Prospects
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        foreach (var prospect in existingProspects)
        {
            if (!string.IsNullOrWhiteSpace(prospect.SourceObjectKey))
            {
                seenKeys.Add(prospect.SourceObjectKey);
            }
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (run.ProspectsSaved >= run.RequestedProspectCount)
            {
                break;
            }

            var pendingRegions = await _db.MapCoverageRegions
                .Where(x => x.ResearchRunId == run.Id && x.Status == MapCoverageRegionStatus.Pending)
                .ToListAsync(cancellationToken);
            var region = pendingRegions.OrderBy(x => x.CreatedAtUtc).FirstOrDefault();
            if (region is null)
            {
                break;
            }

            await ProcessRegionAsync(run, project, plan, region, existingProspects, seenKeys, cancellationToken);
            UpdateCoverageSummary(run);
            run.ProgressPercent = CalculateProgress(run);
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(run, project.Name, cancellationToken);
        }
    }

    private async Task ProcessRegionAsync(
        ResearchRun run,
        OutreachProject project,
        MapResearchPlan plan,
        MapCoverageRegion region,
        List<Prospect> existingProspects,
        HashSet<string> seenKeys,
        CancellationToken cancellationToken)
    {
        region.Status = MapCoverageRegionStatus.InProgress;
        region.StartedAtUtc = DateTimeOffset.UtcNow;
        region.AttemptCount++;
        await _db.SaveChangesAsync(cancellationToken);
        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.MapRegionStarted,
            ResearchRunEventSeverity.Info,
            $"Map region started: {region.RegionLabel} / {region.KeywordFamilyKey}",
            region.RegionLabel,
            cancellationToken);

        var family = plan.KeywordFamilies.FirstOrDefault(x =>
            string.Equals(x.FamilyKey, region.KeywordFamilyKey, StringComparison.OrdinalIgnoreCase));
        var categories = ReadCategories(region.SourceCategoriesJson);
        var request = new MapSourceQueryRequest
        {
            South = region.South,
            West = region.West,
            North = region.North,
            East = region.East,
            SourceCategories = categories,
            NameKeywords = categories.Count > 0
                ? []
                : SelectCompactNameKeywords(family),
            ExcludedKeywords = plan.Specification.ExcludedKeywords
                .Concat(plan.Specification.ExcludedIndustries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            KeywordFamilyKey = region.KeywordFamilyKey,
            CountryCode = region.CountryCode,
            Limit = 50
        };

        if (!_mapSource.IsEnabled)
        {
            region.Status = MapCoverageRegionStatus.Failed;
            region.LastError = "Map source unavailable.";
            region.CompletedAtUtc = DateTimeOffset.UtcNow;
            await RecordEventAsync(run.Id, ResearchRunEventType.MapRegionFailed, ResearchRunEventSeverity.Warning,
                region.LastError, region.RegionLabel, cancellationToken);
            return;
        }

        var queryResult = await _mapSource.QueryAsync(request, cancellationToken);
        if (queryResult.IsRateLimited)
        {
            if (queryResult.ShouldSubdivide
                && GeographicCoveragePlanner.CanSubdivide(region, run.MapRegionsTotal, plan.MaxOverpassRequests))
            {
                await SubdivideRegionAsync(run, region, plan.MaxOverpassRequests, cancellationToken);
                return;
            }

            if (region.AttemptCount >= 3)
            {
                region.Status = MapCoverageRegionStatus.Failed;
                region.LastError = queryResult.ErrorSummary ?? "Overpass rate limited repeatedly.";
                region.CompletedAtUtc = DateTimeOffset.UtcNow;
                await RecordEventAsync(run.Id, ResearchRunEventType.MapRegionFailed, ResearchRunEventSeverity.Warning,
                    region.LastError, region.RegionLabel, cancellationToken);
                return;
            }

            region.Status = MapCoverageRegionStatus.Pending;
            region.LastError = queryResult.ErrorSummary;
            var delaySeconds = queryResult.RetryAfterSeconds ?? 15;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delaySeconds, 5, 30)), cancellationToken);
            return;
        }

        if (!queryResult.Succeeded)
        {
            if (queryResult.IsUnsupported)
            {
                region.Status = MapCoverageRegionStatus.Unsupported;
                region.CoverageNotes = queryResult.ErrorSummary;
                region.CompletedAtUtc = DateTimeOffset.UtcNow;
                await RecordEventAsync(run.Id, ResearchRunEventType.MapSourceUnsupported, ResearchRunEventSeverity.Warning,
                    queryResult.ErrorSummary ?? "Unsupported map query.", region.RegionLabel, cancellationToken);
                return;
            }

            if (queryResult.ShouldSubdivide
                && GeographicCoveragePlanner.CanSubdivide(region, run.MapRegionsTotal, plan.MaxOverpassRequests))
            {
                await SubdivideRegionAsync(run, region, plan.MaxOverpassRequests, cancellationToken);
                return;
            }

            region.Status = MapCoverageRegionStatus.Failed;
            region.LastError = queryResult.ErrorSummary;
            region.CompletedAtUtc = DateTimeOffset.UtcNow;
            await RecordEventAsync(run.Id, ResearchRunEventType.MapRegionFailed, ResearchRunEventSeverity.Warning,
                region.LastError ?? "Map region failed.", region.RegionLabel, cancellationToken);
            return;
        }

        var places = MapPlaceDeduplicator.Deduplicate(queryResult.Places);
        region.RecordsFound = queryResult.Places.Count;
        region.UniqueRecordsFound = places.Count;
        run.MapPlacesDiscovered += places.Count;
        run.CandidatesDiscovered += places.Count;

        if (queryResult.ShouldSubdivide
            && GeographicCoveragePlanner.IsOversized(region, places.Count)
            && GeographicCoveragePlanner.CanSubdivide(region, run.MapRegionsTotal, plan.MaxOverpassRequests))
        {
            await SubdivideRegionAsync(run, region, plan.MaxOverpassRequests, cancellationToken);
            return;
        }

        foreach (var place in places)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (run.ProspectsSaved >= run.RequestedProspectCount)
            {
                break;
            }

            await PersistPlaceAsync(run, project, plan, place, existingProspects, seenKeys, cancellationToken);
        }

        region.Status = MapCoverageRegionStatus.Completed;
        region.CompletedAtUtc = DateTimeOffset.UtcNow;
        run.MapRegionsCompleted = await _db.MapCoverageRegions.CountAsync(
            x => x.ResearchRunId == run.Id && x.Status == MapCoverageRegionStatus.Completed,
            cancellationToken);
        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.MapRegionCompleted,
            ResearchRunEventSeverity.Info,
            $"Map region completed: {region.RegionLabel}; unique={region.UniqueRecordsFound}",
            region.RegionLabel,
            cancellationToken);
    }

    private async Task PersistPlaceAsync(
        ResearchRun run,
        OutreachProject project,
        MapResearchPlan plan,
        MapPlaceRecord place,
        List<Prospect> existingProspects,
        HashSet<string> seenKeys,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(place.SourceObjectKey) && !seenKeys.Add(place.SourceObjectKey))
        {
            run.ProspectsSkippedAsDuplicates++;
            run.DuplicatesFound++;
            return;
        }

        var qualification = MapPlaceQualifier.Evaluate(place, plan);
        if (!qualification.Accepted)
        {
            run.RejectedCount++;
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.MapPlaceRejected,
                ResearchRunEventSeverity.Info,
                $"{place.Name}: {qualification.Reason}",
                place.SourceUrl,
                cancellationToken);
            return;
        }

        var existing = MapPlaceDeduplicator.FindExistingProspect(existingProspects, place);
        if (existing is not null)
        {
            ApplyPlaceToProspect(existing, place, qualification, run.Id);
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            run.ProspectsSkippedAsDuplicates++;
            run.DuplicatesFound++;
            await PersistEvidenceAsync(run, project.Id, existing, place);
            await _contactEnrichment.EnrichAsync(run, project, existing, plan, cancellationToken);
            return;
        }

        var prospect = CreateProspect(project, run, place, qualification);
        _db.Prospects.Add(prospect);
        existingProspects.Add(prospect);
        run.ProspectsSaved++;
        run.CompaniesFound++;
        run.CandidatesAfterDeduplication++;
        run.CandidatesValidated++;
        if (!string.IsNullOrWhiteSpace(prospect.Phone) || !string.IsNullOrWhiteSpace(prospect.Email))
        {
            run.ContactsFound++;
        }

        if (qualification.NeedsReview)
        {
            prospect.Status = ProspectStatus.NeedsReview;
        }
        else
        {
            ApplyFormalQualification(run, project, plan, prospect);
        }

        await PersistEvidenceAsync(run, project.Id, prospect, place);
        await _contactEnrichment.EnrichAsync(run, project, prospect, plan, cancellationToken);
        if (!string.IsNullOrWhiteSpace(prospect.Phone) || !string.IsNullOrWhiteSpace(prospect.Email))
        {
            run.ContactsFound = Math.Max(run.ContactsFound, 1);
        }

        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.MapPlaceDiscovered,
            ResearchRunEventSeverity.Info,
            $"{prospect.CompanyName} @ {prospect.Latitude},{prospect.Longitude}",
            place.SourceUrl,
            cancellationToken);
    }

    private void ApplyFormalQualification(
        ResearchRun run,
        OutreachProject project,
        MapResearchPlan plan,
        Prospect prospect)
    {
        var request = new QualificationEvaluationRequest
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            MinQualificationScore = run.MinQualificationScore,
            RequireEmail = false,
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = plan.Specification.ExcludedKeywords.ToList(),
            Industries = plan.Specification.ApprovedIndustries.ToList(),
            ExcludedIndustries = plan.Specification.ExcludedIndustries.ToList(),
            Countries = plan.Specification.TargetCountries.ToList(),
            Candidate = new ProspectCandidate
            {
                CompanyName = prospect.CompanyName,
                Website = prospect.Website,
                Country = prospect.Country,
                City = prospect.City,
                Industry = prospect.Industry,
                CompanyDescription = prospect.CompanyDescription,
                Phone = prospect.Phone,
                Address = prospect.StreetAddress,
                SourceUrl = prospect.SourceUrl,
                SourceName = prospect.SourceName,
                SourceType = ProspectSourceType.OpenStreetMap,
                ResearchNotes = prospect.ResearchNotes
            }
        };

        var result = _qualificationService.Evaluate(request);
        prospect.QualificationScore = result.Score;
        prospect.QualificationSummary = result.Summary;
        prospect.IsQualified = result.IsQualified;
        prospect.Status = result.IsQualified ? ProspectStatus.Qualified : ProspectStatus.NeedsReview;
        if (result.IsQualified)
        {
            run.QualifiedCount++;
        }

        foreach (var criterion in result.Criteria)
        {
            criterion.ProjectId = project.Id;
            criterion.ResearchRunId = run.Id;
            criterion.ProspectId = prospect.Id;
            _db.QualificationCriterionResults.Add(criterion);
        }
    }

    private async Task SubdivideRegionAsync(
        ResearchRun run,
        MapCoverageRegion region,
        int maxRegions,
        CancellationToken cancellationToken)
    {
        region.Status = MapCoverageRegionStatus.Subdivided;
        region.CompletedAtUtc = DateTimeOffset.UtcNow;
        var remaining = Math.Max(0, maxRegions - run.MapRegionsTotal);
        var children = GeographicCoveragePlanner.Subdivide(region, Math.Clamp(remaining, 0, 4));
        if (children.Count == 0)
        {
            region.Status = MapCoverageRegionStatus.Failed;
            region.LastError = "Subdivision budget exhausted.";
            await RecordEventAsync(
                run.Id,
                ResearchRunEventType.MapRegionFailed,
                ResearchRunEventSeverity.Warning,
                region.LastError,
                region.RegionLabel,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        foreach (var child in children)
        {
            _db.MapCoverageRegions.Add(child);
        }

        run.MapRegionsTotal += children.Count;
        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.MapRegionSubdivided,
            ResearchRunEventSeverity.Info,
            $"Subdivided {region.RegionLabel} into {children.Count} cells.",
            region.RegionLabel,
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static List<string> SelectCompactNameKeywords(MapKeywordFamily? family)
    {
        if (family is null)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var term in family.Terms)
        {
            if (term.SearchUse is not (KeywordSearchUses.PrimaryDiscovery or KeywordSearchUses.AlternateDiscovery))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(term.Term) || term.Term.Length < 3 || term.Term.Length > 24)
            {
                continue;
            }

            if (term.Term.Contains(' ', StringComparison.Ordinal) && term.Kind != KeywordTermKinds.Exact)
            {
                continue;
            }

            result.Add(term.Term.Trim());
            if (result.Count >= 2)
            {
                break;
            }
        }

        return result;
    }

    private async Task EnsureCoverageRegionsAsync(
        ResearchRun run,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        var existingCount = await _db.MapCoverageRegions.CountAsync(x => x.ResearchRunId == run.Id, cancellationToken);
        if (existingCount > 0)
        {
            run.MapRegionsTotal = existingCount;
            return;
        }

        var entities = GeographicCoveragePlanner.ToEntities(run.ProjectId, run.Id, plan.GeographicCells);
        foreach (var entity in entities)
        {
            _db.MapCoverageRegions.Add(entity);
        }

        run.MapRegionsTotal = entities.Count;
        await _db.SaveChangesAsync(cancellationToken);
        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.MapCoverageUpdated,
            ResearchRunEventSeverity.Info,
            $"Persisted {entities.Count} geographic coverage regions.",
            null,
            cancellationToken);
    }

    private async Task<MapResearchPlan> LoadOrBuildPlanAsync(
        ResearchRun run,
        OutreachProject project,
        ResearchSpecification spec,
        ResearchBudgetSpec budgets,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(run.MapPlanJson))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<MapResearchPlan>(run.MapPlanJson, JsonOptions);
                if (existing is not null)
                {
                    MapResearchPlanValidator.Validate(existing);
                    return existing;
                }
            }
            catch (JsonException)
            {
            }
        }

        var plan = await _planBuilder.BuildAsync(project, spec, budgets, cancellationToken);
        run.MapPlanJson = JsonSerializer.Serialize(plan, JsonOptions);
        await _db.SaveChangesAsync(cancellationToken);
        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.MapPlanGenerated,
            ResearchRunEventSeverity.Info,
            $"Map plan generated. Families={plan.KeywordFamilies.Count}; cells={plan.GeographicCells.Count}; valid={plan.IsValid}",
            null,
            cancellationToken);
        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.MapPlanValidated,
            plan.IsValid ? ResearchRunEventSeverity.Info : ResearchRunEventSeverity.Error,
            plan.IsValid
                ? "Map plan validated."
                : string.Join("; ", plan.ValidationIssues.Select(x => x.Message)),
            null,
            cancellationToken);
        return plan;
    }

    private async Task<ResearchSpecification> ResolveSpecificationAsync(
        ResearchRun run,
        OutreachProject project,
        CancellationToken cancellationToken)
    {
        ResearchQueryVersion? version = null;
        if (run.ResearchQueryVersionId is not null)
        {
            version = await _db.ResearchQueryVersions.FirstOrDefaultAsync(
                x => x.Id == run.ResearchQueryVersionId.Value,
                cancellationToken);
        }

        var persisted = ProjectUnderstandingEngine.TryRead(version);
        if (persisted is not null)
        {
            return persisted;
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
            MinQualificationScore = run.MinQualificationScore,
            TargetProspectCount = run.RequestedProspectCount,
            IncludeEmail = run.IncludeEmail,
            IncludeWhatsApp = run.IncludeWhatsApp,
            QueryRequireOfficialWebsite = false,
            QueryRequirePublicBusinessEmail = false,
            QueryCities = project.Cities.Select(x => x.Name).ToList(),
            Budgets = BuildBudgets(run, null)
        };

        if (version is not null)
        {
            request.QueryTargetIndustry = version.TargetIndustry;
            request.QueryTargetCountry = version.TargetCountry;
            var versionCities = ProjectUnderstandingEngine.ReadJsonList(version.CitiesJson);
            if (versionCities.Count > 0)
            {
                request.QueryCities = versionCities;
            }
            request.QueryBusinessCategories = ProjectUnderstandingEngine.ReadJsonList(version.BusinessCategoriesJson);
            request.QueryExclusionCriteria = ProjectUnderstandingEngine.ReadJsonList(version.ExclusionCriteriaJson);
            request.QueryRequireOfficialWebsite = version.RequireOfficialWebsite;
            request.QueryRequirePublicBusinessEmail = false;
        }

        var spec = ProjectUnderstandingEngine.Interpret(request);
        if (version is not null)
        {
            ProjectUnderstandingEngine.ApplyToQueryVersion(version, spec);
            await _db.SaveChangesAsync(cancellationToken);
        }

        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.ProjectInterpreted,
            ResearchRunEventSeverity.Info,
            $"Project interpreted for map research. Industries={string.Join(',', spec.ApprovedIndustries)}; countries={string.Join(',', spec.TargetCountries)}",
            null,
            cancellationToken);
        return spec;
    }

    private ResearchBudgetSpec BuildBudgets(ResearchRun run, ResearchSpecification? spec)
    {
        return new ResearchBudgetSpec
        {
            MaxUrlsPerRun = Math.Max(run.RequestedProspectCount * 2, _options.MaxCandidatesPerRun),
            MaxSearchRequests = Math.Clamp(_options.MaxSearchRequests, 5, 80),
            MaxSearchQueries = _options.MaxSearchQueries,
            MaxFetchesPerRun = _options.MaxFetchesPerRun,
            MaxConcurrentRequests = _options.MaxConcurrentRequests,
            MaxResearchDurationMinutes = _options.MaxResearchDurationMinutes,
            MaxPagesPerDomain = spec?.Budgets.MaxPagesPerDomain ?? 5,
            MaxCrawlDepth = spec?.Budgets.MaxCrawlDepth ?? 2
        };
    }

    private async Task FinalizeRunAsync(ResearchRun run, string projectName, CancellationToken cancellationToken)
    {
        if (run.RequestedProspectCount > 0 && run.ProspectsSaved >= run.RequestedProspectCount)
        {
            await CancelRemainingPendingRegionsAsync(run, cancellationToken);
        }

        UpdateCoverageSummary(run);
        var pending = await _db.MapCoverageRegions.CountAsync(
            x => x.ResearchRunId == run.Id && x.Status == MapCoverageRegionStatus.Pending,
            cancellationToken);
        var failed = await _db.MapCoverageRegions.CountAsync(
            x => x.ResearchRunId == run.Id && x.Status == MapCoverageRegionStatus.Failed,
            cancellationToken);
        run.ProgressPercent = 100;
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        run.Status = ResolveMapOutcome(run, pending, failed);
        await _db.SaveChangesAsync(cancellationToken);
        await RecordEventAsync(
            run.Id,
            ResearchRunEventType.Completed,
            ResearchRunEventSeverity.Info,
            $"Map research finished. Places={run.MapPlacesDiscovered}; saved={run.ProspectsSaved}; regionsCompleted={run.MapRegionsCompleted}/{run.MapRegionsTotal}",
            null,
            cancellationToken);
        await NotifyAsync(run, projectName, cancellationToken);
    }

    private static ResearchRunStatus ResolveMapOutcome(ResearchRun run, int pending, int failed)
    {
        if (run.RequestedProspectCount > 0 && run.ProspectsSaved >= run.RequestedProspectCount)
        {
            return failed > 0 ? ResearchRunStatus.CompletedWithFailures : ResearchRunStatus.Completed;
        }

        if (pending > 0)
        {
            return ResearchRunStatus.Partial;
        }

        if (run.ProspectsSaved > 0)
        {
            return failed > 0 ? ResearchRunStatus.CompletedWithFailures : ResearchRunStatus.Completed;
        }

        return failed > 0 ? ResearchRunStatus.Failed : ResearchRunStatus.Partial;
    }

    private async Task CancelRemainingPendingRegionsAsync(ResearchRun run, CancellationToken cancellationToken)
    {
        var pendingRegions = await _db.MapCoverageRegions
            .Where(x => x.ResearchRunId == run.Id && x.Status == MapCoverageRegionStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (var region in pendingRegions)
        {
            region.Status = MapCoverageRegionStatus.Cancelled;
            region.CoverageNotes = "Skipped because the requested prospect target was already reached.";
            region.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private void UpdateCoverageSummary(ResearchRun run)
    {
        var summary = new
        {
            regionsTotal = run.MapRegionsTotal,
            regionsCompleted = run.MapRegionsCompleted,
            placesDiscovered = run.MapPlacesDiscovered,
            prospectsSaved = run.ProspectsSaved,
            source = _mapSource.Name
        };
        run.MapCoverageSummaryJson = JsonSerializer.Serialize(summary, JsonOptions);
    }

    private static Prospect CreateProspect(
        OutreachProject project,
        ResearchRun run,
        MapPlaceRecord place,
        MapPlaceQualificationResult qualification)
    {
        var prospect = new Prospect
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            CompanyName = place.Name.Trim(),
            NormalizedCompanyName = ProspectNormalizer.NormalizeCompanyName(place.Name),
            Website = place.Website,
            NormalizedWebsite = UrlCanonicalizer.ExtractDomain(place.Website),
            Country = place.Country,
            City = place.City,
            NormalizedCity = ProspectNormalizer.NormalizeCity(place.City),
            StreetAddress = BuildFullStreetAddress(place),
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            OpeningHours = place.OpeningHours,
            OsmElementType = place.OsmElementType,
            OsmElementId = place.OsmElementId,
            SourceObjectKey = place.SourceObjectKey,
            CategoryTagsJson = place.CategoryTags,
            RawOsmTagsJson = place.RawTagsJson,
            Industry = qualification.AssignedIndustry,
            CompanyDescription = BuildDescription(place),
            Phone = place.Phone,
            NormalizedPhone = ProspectNormalizer.NormalizePhone(place.Phone),
            Email = place.Email,
            NormalizedEmail = ProspectNormalizer.NormalizeEmail(place.Email),
            Fax = place.Fax,
            SourceUrl = place.SourceUrl,
            SourceType = ProspectSourceType.OpenStreetMap,
            SourceName = place.SourceName,
            ResearchNotes = qualification.Reason,
            Status = ProspectStatus.Discovered
        };
        ApplyAdditionalContacts(prospect, place);
        return prospect;
    }

    private static void ApplyPlaceToProspect(
        Prospect prospect,
        MapPlaceRecord place,
        MapPlaceQualificationResult qualification,
        Guid researchRunId)
    {
        prospect.ResearchRunId ??= researchRunId;
        prospect.Latitude ??= place.Latitude;
        prospect.Longitude ??= place.Longitude;
        if (string.IsNullOrWhiteSpace(prospect.StreetAddress))
        {
            prospect.StreetAddress = BuildFullStreetAddress(place);
        }

        prospect.OpeningHours ??= place.OpeningHours;
        if (string.IsNullOrWhiteSpace(prospect.Website) && !string.IsNullOrWhiteSpace(place.Website))
        {
            prospect.Website = place.Website;
            prospect.NormalizedWebsite = UrlCanonicalizer.ExtractDomain(place.Website);
        }

        if (string.IsNullOrWhiteSpace(prospect.Phone) && !string.IsNullOrWhiteSpace(place.Phone))
        {
            prospect.Phone = place.Phone;
            prospect.NormalizedPhone = ProspectNormalizer.NormalizePhone(place.Phone);
        }

        if (string.IsNullOrWhiteSpace(prospect.Email) && !string.IsNullOrWhiteSpace(place.Email))
        {
            prospect.Email = place.Email;
            prospect.NormalizedEmail = ProspectNormalizer.NormalizeEmail(place.Email);
        }

        prospect.Fax ??= place.Fax;
        prospect.OsmElementType ??= place.OsmElementType;
        prospect.OsmElementId ??= place.OsmElementId;
        prospect.SourceObjectKey ??= place.SourceObjectKey;
        prospect.CategoryTagsJson ??= place.CategoryTags;
        prospect.RawOsmTagsJson ??= place.RawTagsJson;
        prospect.Industry ??= qualification.AssignedIndustry;
        ApplyAdditionalContacts(prospect, place);
        if (string.IsNullOrWhiteSpace(prospect.SourceUrl))
        {
            prospect.SourceUrl = place.SourceUrl;
        }
    }

    private static void ApplyAdditionalContacts(Prospect prospect, MapPlaceRecord place)
    {
        var phones = place.Phones
            .Select(value => new AttributedContact
            {
                Value = value,
                Channel = "phone",
                SourceUrl = place.SourceUrl,
                VerificationStatus = "source_reported",
                AttributionReasonCode = "map.osm_tag",
                Confidence = 90
            });
        prospect.AdditionalPhonesJson = ProspectContactStore.MergeAttributedPhones(
            prospect.AdditionalPhonesJson,
            phones);

        var emails = place.Emails
            .Select(value => new AttributedContact
            {
                Value = value,
                Channel = "email",
                SourceUrl = place.SourceUrl,
                VerificationStatus = "source_reported",
                AttributionReasonCode = "map.osm_tag",
                Confidence = 90
            });
        prospect.AdditionalEmailsJson = ProspectContactStore.MergeAttributedEmails(
            prospect.AdditionalEmailsJson,
            emails);
    }

    private static string? BuildFullStreetAddress(MapPlaceRecord place)
    {
        if (string.IsNullOrWhiteSpace(place.StreetAddress) && string.IsNullOrWhiteSpace(place.Postcode))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(place.Postcode))
        {
            return place.StreetAddress;
        }

        if (string.IsNullOrWhiteSpace(place.StreetAddress))
        {
            return place.Postcode;
        }

        return $"{place.StreetAddress}, {place.Postcode}";
    }

    private Task PersistEvidenceAsync(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        MapPlaceRecord place)
    {
        foreach (var field in place.FieldProvenance.Values)
        {
            _db.ResearchEvidenceItems.Add(new ResearchEvidence
            {
                ProjectId = projectId,
                ResearchRunId = run.Id,
                ProspectId = prospect.Id,
                FactType = $"map.{field.FieldName}",
                FactValue = field.Value,
                ConfidenceKind = EvidenceConfidenceKind.Verified,
                SourceUrl = field.SourceUrl,
                Notes = BuildEvidenceNotes(field),
                FirstDiscoveredAtUtc = field.RetrievedAtUtc,
                LastVerifiedAtUtc = field.RetrievedAtUtc
            });
        }

        if (!string.IsNullOrWhiteSpace(place.RawTagsJson))
        {
            _db.ResearchEvidenceItems.Add(new ResearchEvidence
            {
                ProjectId = projectId,
                ResearchRunId = run.Id,
                ProspectId = prospect.Id,
                FactType = "map.rawTags",
                FactValue = Trim(place.RawTagsJson, 4000),
                ConfidenceKind = EvidenceConfidenceKind.Verified,
                SourceUrl = place.SourceUrl,
                Notes = $"{place.SourceObjectKey}; source_reported; structured_tag",
                FirstDiscoveredAtUtc = DateTimeOffset.UtcNow,
                LastVerifiedAtUtc = DateTimeOffset.UtcNow
            });
        }

        return Task.CompletedTask;
    }

    private static string BuildEvidenceNotes(MapFieldProvenance field)
    {
        var parts = new List<string>
        {
            field.SourceIdentifier,
            field.VerificationStatus,
            field.ExtractionMethod
        };
        if (!string.IsNullOrWhiteSpace(field.SourceTagKey))
        {
            parts.Add($"tag={field.SourceTagKey}");
        }

        if (!string.IsNullOrWhiteSpace(field.SupportingExcerpt))
        {
            parts.Add($"excerpt={Trim(field.SupportingExcerpt, 200)}");
        }

        return string.Join("; ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private async Task RecordEventAsync(
        Guid researchRunId,
        ResearchRunEventType eventType,
        ResearchRunEventSeverity severity,
        string? message,
        string? url,
        CancellationToken cancellationToken)
    {
        await _events.RecordAsync(
            new ResearchEventRecordRequest
            {
                ResearchRunId = researchRunId,
                EventType = eventType,
                Severity = severity,
                Stage = ResearchPipelineStage.RunLifecycle,
                Message = message,
                Url = url,
                Provider = _mapSource.Name
            },
            cancellationToken);
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

    private async Task FailRunAsync(ResearchRun run, string error, CancellationToken cancellationToken)
    {
        run.Status = ResearchRunStatus.Failed;
        run.Error = error;
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _cancellationRegistry.Clear(run.Id);
    }

    private async Task NotifyAsync(ResearchRun run, string? projectName, CancellationToken cancellationToken)
    {
        var prospects = await _db.Prospects.AsNoTracking()
            .Where(x => x.ResearchRunId == run.Id)
            .ToListAsync(cancellationToken);
        prospects = prospects
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToList();
        var detail = ResearchRunMapper.MapDetail(run, projectName, prospects);
        await _progressNotifier.NotifyProgressAsync(detail, cancellationToken);
    }

    private static int CalculateProgress(ResearchRun run)
    {
        if (run.MapRegionsTotal <= 0)
        {
            return Math.Clamp(run.ProspectsSaved * 100 / Math.Max(1, run.RequestedProspectCount), 1, 99);
        }

        var regionProgress = run.MapRegionsCompleted * 100 / Math.Max(1, run.MapRegionsTotal);
        return Math.Clamp(regionProgress, 1, 99);
    }

    private static List<string> ReadCategories(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string BuildDescription(MapPlaceRecord place)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(place.CategoryTags))
        {
            parts.Add(place.CategoryTags);
        }

        if (!string.IsNullOrWhiteSpace(place.StreetAddress))
        {
            parts.Add(place.StreetAddress);
        }

        if (!string.IsNullOrWhiteSpace(place.OpeningHours))
        {
            parts.Add($"Hours: {place.OpeningHours}");
        }

        return Trim(string.Join(" | ", parts), 2000);
    }

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max];
    }
}
