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
            Status = run.Status,
            RequestedProspectCount = run.RequestedProspectCount,
            IncludeEmail = run.IncludeEmail,
            IncludeWhatsApp = run.IncludeWhatsApp,
            MinQualificationScore = run.MinQualificationScore,
            ProgressPercent = run.ProgressPercent,
            CreatedAtUtc = run.CreatedAtUtc
        };
    }

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

        if (run.Status is ResearchRunStatus.Completed
            or ResearchRunStatus.CompletedWithFailures
            or ResearchRunStatus.Failed
            or ResearchRunStatus.Cancelled)
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
