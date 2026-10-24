using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Application.Services;

public class ResearchRunService
{
    private readonly ISendlyDbContext _db;
    private readonly IResearchQueue _queue;
    private readonly ResearchOptions _options;
    private readonly ResearchCancellationRegistry _cancellationRegistry;
    private readonly ILogger<ResearchRunService> _logger;

    public ResearchRunService(
        ISendlyDbContext db,
        IResearchQueue queue,
        IOptions<ResearchOptions> options,
        ResearchCancellationRegistry cancellationRegistry,
        ILogger<ResearchRunService> logger)
    {
        _db = db;
        _queue = queue;
        _options = options.Value;
        _cancellationRegistry = cancellationRegistry;
        _logger = logger;
    }

    public async Task<ResearchRunDetailDto> StartAsync(
        Guid projectId,
        StartResearchRequestDto request,
        CancellationToken cancellationToken)
    {
        var project = await _db.OutreachProjects.FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        if (!project.IsActive)
        {
            throw new InvalidOperationException("Cannot start research on an inactive project.");
        }

        var includeEmail = request.IncludeEmail;
        var includeWhatsApp = request.IncludeWhatsApp;
        if (!includeEmail && !includeWhatsApp)
        {
            includeEmail = project.EnableEmail;
            includeWhatsApp = project.EnableWhatsApp;
        }

        if (!includeEmail && !includeWhatsApp)
        {
            throw new InvalidOperationException("At least one contact method is required.");
        }

        var queryBinding = await ResolveOrCreateQueryBindingAsync(projectId, request, cancellationToken);
        EnsureResearchTaskIsDefined(projectId, queryBinding);

        var requested = request.RequestedProspectCount > 0
            ? request.RequestedProspectCount
            : project.DefaultRequestedProspectCount;
        requested = Math.Clamp(requested, 1, _options.MaxCandidatesPerRun);

        var minScore = request.MinQualificationScore
            ?? project.MinQualificationScore;
        minScore = Math.Clamp(minScore, 0, 100);

        var run = new ResearchRun
        {
            ProjectId = projectId,
            ResearchQueryId = queryBinding.QueryId,
            ResearchQueryVersionId = queryBinding.VersionId,
            Status = ResearchRunStatus.Pending,
            RequestedProspectCount = requested,
            IncludeEmail = includeEmail,
            IncludeWhatsApp = includeWhatsApp,
            MinQualificationScore = minScore,
            ProgressPercent = 0
        };

        _db.ResearchRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);
        await _queue.EnqueueAsync(run.Id, cancellationToken);
        _logger.LogInformation("Research started {ResearchRunId} for project {ProjectId}", run.Id, projectId);
        return new ResearchRunDetailDto
        {
            Id = run.Id,
            ProjectId = run.ProjectId,
            ProjectName = project.Name,
            ResearchQueryId = run.ResearchQueryId,
            ResearchQueryVersionId = run.ResearchQueryVersionId,
            ResearchQueryName = queryBinding.QueryName,
            Status = run.Status,
            RequestedProspectCount = run.RequestedProspectCount,
            IncludeEmail = run.IncludeEmail,
            IncludeWhatsApp = run.IncludeWhatsApp,
            MinQualificationScore = run.MinQualificationScore,
            ProgressPercent = run.ProgressPercent,
            CreatedAtUtc = run.CreatedAtUtc
        };
    }

    private async Task<ResearchQueryBinding> ResolveOrCreateQueryBindingAsync(
        Guid projectId,
        StartResearchRequestDto request,
        CancellationToken cancellationToken)
    {
        ResearchQuery? query = null;
        if (request.ResearchQueryId is not null)
        {
            query = await _db.ResearchQueries.FirstOrDefaultAsync(
                x => x.Id == request.ResearchQueryId.Value && x.ProjectId == projectId && !x.IsArchived,
                cancellationToken);
            if (query is null)
            {
                throw new InvalidOperationException("Research query was not found for this project.");
            }
        }
        else
        {
            var queries = await _db.ResearchQueries
                .Where(x => x.ProjectId == projectId && !x.IsArchived)
                .ToListAsync(cancellationToken);
            query = queries.OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
        }

        if (query is null)
        {
            return await CreateDefaultQueryFromProjectAsync(projectId, cancellationToken);
        }

        ResearchQueryVersion? version = null;
        if (request.ResearchQueryVersionId is not null)
        {
            version = await _db.ResearchQueryVersions.FirstOrDefaultAsync(
                x => x.Id == request.ResearchQueryVersionId.Value && x.ResearchQueryId == query.Id,
                cancellationToken);
            if (version is null)
            {
                throw new InvalidOperationException("Research query version was not found.");
            }
        }
        else if (query.ActiveVersionId is not null)
        {
            version = await _db.ResearchQueryVersions.FirstOrDefaultAsync(
                x => x.Id == query.ActiveVersionId.Value,
                cancellationToken);
        }

        if (version is null)
        {
            version = await _db.ResearchQueryVersions
                .Where(x => x.ResearchQueryId == query.Id)
                .OrderByDescending(x => x.VersionNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (version is not null)
        {
            await RepairQueryVersionAgainstProjectAsync(projectId, version, cancellationToken);
        }

        return new ResearchQueryBinding(
            query.Id,
            version?.Id,
            query.Name,
            version?.TargetCountry,
            version?.TargetIndustry,
            version?.EnableDirectoryDiscovery ?? true,
            version?.EnablePriorResearchUrls ?? true,
            version?.EnableSitemapDiscovery ?? true,
            version?.EnableLinkExpansion ?? true);
    }

    private async Task<ResearchQueryBinding> CreateDefaultQueryFromProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await _db.OutreachProjects
            .Include(x => x.Countries)
            .Include(x => x.Industries)
            .Include(x => x.Keywords)
            .Include(x => x.TargetRoles)
            .FirstAsync(x => x.Id == projectId, cancellationToken);

        var spec = BuildSpecification(project, null);
        var query = new ResearchQuery
        {
            ProjectId = projectId,
            Name = "Automatic discovery",
            Description = "System-generated query for automatic research discovery.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        var version = new ResearchQueryVersion
        {
            Id = Guid.NewGuid(),
            ResearchQueryId = query.Id,
            VersionNumber = 1,
            PlainLanguageBrief = project.ResearchInstructions,
            EnableDirectoryDiscovery = true,
            EnableSitemapDiscovery = true,
            EnableLinkExpansion = true,
            EnablePriorResearchUrls = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        ProjectUnderstandingEngine.ApplyToQueryVersion(version, spec);

        query.ActiveVersionId = version.Id;
        _db.ResearchQueries.Add(query);
        _db.ResearchQueryVersions.Add(version);
        await _db.SaveChangesAsync(cancellationToken);

        return new ResearchQueryBinding(
            query.Id,
            version.Id,
            query.Name,
            version.TargetCountry,
            version.TargetIndustry,
            true,
            true,
            true,
            true);
    }

    private void EnsureResearchTaskIsDefined(Guid projectId, ResearchQueryBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.TargetCountry)
            || !string.IsNullOrWhiteSpace(binding.TargetIndustry))
        {
            return;
        }

        var hasProjectScope = _db.OutreachProjectCountries.Any(x => x.ProjectId == projectId)
            || _db.OutreachProjectIndustries.Any(x => x.ProjectId == projectId && !x.IsExcluded)
            || _db.OutreachProjectKeywords.Any(x => x.ProjectId == projectId && !x.IsExcluded);

        if (hasProjectScope)
        {
            return;
        }

        throw new InvalidOperationException(
            "Define a target industry or country before running research. Seed URLs are not required.");
    }

    private async Task RepairQueryVersionAgainstProjectAsync(
        Guid projectId,
        ResearchQueryVersion version,
        CancellationToken cancellationToken)
    {
        var project = await _db.OutreachProjects
            .Include(x => x.Countries)
            .Include(x => x.Industries)
            .Include(x => x.Keywords)
            .Include(x => x.TargetRoles)
            .FirstAsync(x => x.Id == projectId, cancellationToken);
        var spec = BuildSpecification(project, version);
        if (spec.CorrectionReasons.Count == 0
            && !string.IsNullOrWhiteSpace(version.StructuredDefinitionJson)
            && ProjectUnderstandingEngine.ReadJsonList(version.BusinessCategoriesJson).Count > 1)
        {
            return;
        }

        ProjectUnderstandingEngine.ApplyToQueryVersion(version, spec);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private ResearchSpecification BuildSpecification(OutreachProject project, ResearchQueryVersion? version)
    {
        return ProjectUnderstandingEngine.Interpret(new ProjectUnderstandingRequest
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
            QueryTargetIndustry = version?.TargetIndustry,
            QueryBusinessCategories = ProjectUnderstandingEngine.ReadJsonList(version?.BusinessCategoriesJson),
            QueryTargetCountry = version?.TargetCountry,
            QueryCities = ProjectUnderstandingEngine.ReadJsonList(version?.CitiesJson),
            QueryExclusionCriteria = ProjectUnderstandingEngine.ReadJsonList(version?.ExclusionCriteriaJson),
            QueryRequireOfficialWebsite = version?.RequireOfficialWebsite ?? true,
            QueryRequirePublicBusinessEmail = version?.RequirePublicBusinessEmail ?? project.EnableEmail,
            MinQualificationScore = project.MinQualificationScore,
            TargetProspectCount = project.DefaultRequestedProspectCount,
            IncludeEmail = project.EnableEmail,
            IncludeWhatsApp = project.EnableWhatsApp,
            Budgets = new ResearchBudgetSpec
            {
                MaxUrlsPerRun = Math.Clamp(_options.MaxCandidatesPerRun, 1, 2000),
                MaxPagesPerDomain = Math.Clamp(_options.MaxPagesPerCrawl, 1, 50),
                MaxCrawlDepth = 4,
                MaxSearchQueries = _options.MaxSearchQueries,
                MaxSearchRequests = _options.MaxSearchRequests,
                MaxFetchesPerRun = _options.MaxFetchesPerRun,
                MaxConcurrentRequests = Math.Max(1, _options.MaxConcurrentRequests),
                MaxResearchDurationMinutes = _options.MaxResearchDurationMinutes
            }
        });
    }

    private sealed record ResearchQueryBinding(
        Guid? QueryId,
        Guid? VersionId,
        string? QueryName,
        string? TargetCountry,
        string? TargetIndustry,
        bool EnableDirectoryDiscovery,
        bool EnablePriorResearchUrls,
        bool EnableSitemapDiscovery,
        bool EnableLinkExpansion);

    public async Task<List<ResearchRunSummaryDto>> ListAsync(
        Guid? projectId,
        Guid? templateId,
        CancellationToken cancellationToken)
    {
        var query = _db.ResearchRuns.AsNoTracking().AsQueryable();
        if (projectId is not null)
        {
            query = query.Where(x => x.ProjectId == projectId);
        }

        if (templateId is not null)
        {
            var linkedProjectIds = await LoadProjectIdsForTemplateAsync(templateId.Value, cancellationToken);
            query = query.Where(x => linkedProjectIds.Contains(x.ProjectId));
        }

        var runs = await query.ToListAsync(cancellationToken);
        runs = runs.OrderByDescending(x => x.CreatedAtUtc).ToList();
        var projectIds = runs.Select(x => x.ProjectId).Distinct().ToList();
        var names = await _db.OutreachProjects.AsNoTracking()
            .Where(x => projectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var result = new List<ResearchRunSummaryDto>();
        foreach (var run in runs)
        {
            names.TryGetValue(run.ProjectId, out var name);
            result.Add(ResearchRunMapper.MapSummary(run, name));
        }

        return result;
    }

    private async Task<List<Guid>> LoadProjectIdsForTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        return await _db.ProjectTemplates.AsNoTracking()
            .Where(x => x.TemplateId == templateId)
            .Select(x => x.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<ResearchRunDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await _db.ResearchRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var projectName = await _db.OutreachProjects.AsNoTracking()
            .Where(x => x.Id == run.ProjectId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var prospects = await _db.Prospects.AsNoTracking()
            .Where(x => x.ResearchRunId == run.Id)
            .ToListAsync(cancellationToken);
        prospects = prospects
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToList();

        return ResearchRunMapper.MapDetail(run, projectName, prospects);
    }

    public async Task CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await _db.ResearchRuns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("Research run was not found.");
        }

        if (ResearchRunOutcomeResolver.IsTerminal(run.Status))
        {
            return;
        }

        run.Status = ResearchRunStatus.Cancelled;
        run.Warning = "Research run was cancelled by the user.";
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _cancellationRegistry.Cancel(id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await _db.ResearchRuns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("Research run was not found.");
        }

        if (run.Status is ResearchRunStatus.Pending or ResearchRunStatus.Running)
        {
            _cancellationRegistry.Cancel(id);
        }

        await RemoveRunDependentsAsync(id, cancellationToken);
        _db.ResearchRuns.Remove(run);
        await _db.SaveChangesAsync(cancellationToken);
        _cancellationRegistry.Clear(id);
        _logger.LogInformation("Research run deleted {ResearchRunId}", id);
    }

    private async Task RemoveRunDependentsAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var criterionResults = await _db.QualificationCriterionResults
            .Where(x => x.ResearchRunId == researchRunId)
            .ToListAsync(cancellationToken);
        _db.QualificationCriterionResults.RemoveRange(criterionResults);

        var evidence = await _db.ResearchEvidenceItems
            .Where(x => x.ResearchRunId == researchRunId)
            .ToListAsync(cancellationToken);
        _db.ResearchEvidenceItems.RemoveRange(evidence);

        var pages = await _db.CrawledPages
            .Where(x => x.ResearchRunId == researchRunId)
            .ToListAsync(cancellationToken);
        _db.CrawledPages.RemoveRange(pages);

        var contacts = await _db.InvestigationContacts
            .Where(x => x.ResearchRunId == researchRunId)
            .ToListAsync(cancellationToken);
        _db.InvestigationContacts.RemoveRange(contacts);

        var urls = await _db.DiscoveredUrls
            .Where(x => x.ResearchRunId == researchRunId)
            .ToListAsync(cancellationToken);
        _db.DiscoveredUrls.RemoveRange(urls);

        var events = await _db.ResearchRunEvents
            .Where(x => x.ResearchRunId == researchRunId)
            .ToListAsync(cancellationToken);
        _db.ResearchRunEvents.RemoveRange(events);

        var prospectIds = await _db.Prospects.AsNoTracking()
            .Where(x => x.ResearchRunId == researchRunId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (prospectIds.Count > 0)
        {
            var enrollments = await _db.ProspectSequenceEnrollments
                .Where(x => prospectIds.Contains(x.ProspectId))
                .ToListAsync(cancellationToken);
            _db.ProspectSequenceEnrollments.RemoveRange(enrollments);

            var histories = await _db.OutreachContactHistories
                .Where(x => x.ProspectId != null && prospectIds.Contains(x.ProspectId.Value))
                .ToListAsync(cancellationToken);
            foreach (var history in histories)
            {
                history.ProspectId = null;
            }

            var prospects = await _db.Prospects
                .Where(x => x.ResearchRunId == researchRunId)
                .ToListAsync(cancellationToken);
            _db.Prospects.RemoveRange(prospects);
        }
    }
}
