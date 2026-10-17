using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services;

public class OutreachProjectService
{
    private readonly ISendlyDbContext _db;
    private readonly ILogger<OutreachProjectService> _logger;

    public OutreachProjectService(ISendlyDbContext db, ILogger<OutreachProjectService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<OutreachProjectSummaryDto>> ListAsync(
        string? search,
        Guid? templateId,
        CancellationToken cancellationToken)
    {
        var query = _db.OutreachProjects.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.Contains(term) || (x.Description != null && x.Description.Contains(term)));
        }

        if (templateId is not null)
        {
            var linkedProjectIds = await LoadProjectIdsForTemplateAsync(templateId.Value, cancellationToken);
            query = query.Where(x => linkedProjectIds.Contains(x.Id));
        }

        var projects = await query.ToListAsync(cancellationToken);
        projects = projects.OrderByDescending(x => x.UpdatedAtUtc).ToList();
        var result = new List<OutreachProjectSummaryDto>();
        foreach (var project in projects)
        {
            await LoadProjectCollectionsAsync(
                project,
                new ProjectCollectionLoadOptions(false, false),
                cancellationToken);
            result.Add(await MapSummaryAsync(project, cancellationToken));
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

    public async Task<OutreachProjectDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await _db.OutreachProjects.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        await LoadProjectCollectionsAsync(
            project,
            new ProjectCollectionLoadOptions(true, true),
            cancellationToken);

        var summary = await MapSummaryAsync(project, cancellationToken);
        var detail = new OutreachProjectDetailDto
        {
            Id = summary.Id,
            Name = summary.Name,
            Description = summary.Description,
            ResearchInstructions = project.ResearchInstructions,
            IsActive = summary.IsActive,
            EnableEmail = summary.EnableEmail,
            EnableWhatsApp = summary.EnableWhatsApp,
            Countries = summary.Countries,
            Keywords = summary.Keywords,
            ProspectCount = summary.ProspectCount,
            QualifiedCount = summary.QualifiedCount,
            NeedsReviewCount = summary.NeedsReviewCount,
            RejectedCount = summary.RejectedCount,
            ValidEmailCount = summary.ValidEmailCount,
            ValidWhatsAppCount = summary.ValidWhatsAppCount,
            AlreadyContactedCount = summary.AlreadyContactedCount,
            ReadyForOutreachCount = summary.ReadyForOutreachCount,
            CampaignsCreatedCount = summary.CampaignsCreatedCount,
            LastResearchAtUtc = summary.LastResearchAtUtc,
            CreatedAtUtc = summary.CreatedAtUtc,
            UpdatedAtUtc = summary.UpdatedAtUtc,
            MinQualificationScore = project.MinQualificationScore,
            DuplicateHandling = project.DuplicateHandling,
            DefaultRequestedProspectCount = project.DefaultRequestedProspectCount,
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Industries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            TargetRoles = project.TargetRoles.Select(x => x.Role).ToList(),
            Templates = MapTemplates(project.Templates),
            RecentResearchRuns = await LoadRecentRunsAsync(project.Id, cancellationToken)
        };

        detail.Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList();
        detail.Countries = project.Countries.Select(x => x.Name).ToList();
        return detail;
    }

    public async Task<OutreachProjectDetailDto> CreateAsync(
        UpsertOutreachProjectDto request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        await EnsureTemplatesExistAsync(request.Templates, cancellationToken);

        var project = new OutreachProject
        {
            Name = request.Name.Trim(),
            Description = TrimOrNull(request.Description),
            ResearchInstructions = TrimOrNull(request.ResearchInstructions),
            IsActive = request.IsActive,
            MinQualificationScore = Math.Clamp(request.MinQualificationScore, 0, 100),
            DuplicateHandling = request.DuplicateHandling,
            EnableEmail = request.EnableEmail,
            EnableWhatsApp = request.EnableWhatsApp,
            DefaultRequestedProspectCount = Math.Clamp(request.DefaultRequestedProspectCount, 1, 500)
        };

        ApplyCollections(project, request);
        _db.OutreachProjects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Outreach project created {ProjectId}", project.Id);
        return (await GetAsync(project.Id, cancellationToken))!;
    }

    public async Task<OutreachProjectDetailDto> UpdateAsync(
        Guid id,
        UpsertOutreachProjectDto request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        await EnsureTemplatesExistAsync(request.Templates, cancellationToken);

        var project = await _db.OutreachProjects
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        project.Name = request.Name.Trim();
        project.Description = TrimOrNull(request.Description);
        project.ResearchInstructions = TrimOrNull(request.ResearchInstructions);
        project.IsActive = request.IsActive;
        project.MinQualificationScore = Math.Clamp(request.MinQualificationScore, 0, 100);
        project.DuplicateHandling = request.DuplicateHandling;
        project.EnableEmail = request.EnableEmail;
        project.EnableWhatsApp = request.EnableWhatsApp;
        project.DefaultRequestedProspectCount = Math.Clamp(request.DefaultRequestedProspectCount, 1, 500);
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await ReplaceProjectCollectionsAsync(project, request, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return (await GetAsync(project.Id, cancellationToken))!;
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await _db.OutreachProjects.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        project.IsActive = false;
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await _db.OutreachProjects.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        await RemoveProjectDependentsAsync(id, cancellationToken);
        _db.OutreachProjects.Remove(project);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Outreach project deleted {ProjectId}", id);
    }

    private async Task RemoveProjectDependentsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var enrollments = await _db.ProspectSequenceEnrollments
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        _db.ProspectSequenceEnrollments.RemoveRange(enrollments);

        var sequenceIds = await _db.OutreachSequences.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (sequenceIds.Count > 0)
        {
            var steps = await _db.OutreachSequenceSteps
                .Where(x => sequenceIds.Contains(x.SequenceId))
                .ToListAsync(cancellationToken);
            _db.OutreachSequenceSteps.RemoveRange(steps);
        }

        var sequences = await _db.OutreachSequences
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        _db.OutreachSequences.RemoveRange(sequences);

        var prospects = await _db.Prospects
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        _db.Prospects.RemoveRange(prospects);

        var runs = await _db.ResearchRuns
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        _db.ResearchRuns.RemoveRange(runs);

        var templates = await _db.ProjectTemplates
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        _db.ProjectTemplates.RemoveRange(templates);

        var keywords = await _db.OutreachProjectKeywords
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        _db.OutreachProjectKeywords.RemoveRange(keywords);

        var countries = await _db.OutreachProjectCountries
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        _db.OutreachProjectCountries.RemoveRange(countries);

        var industries = await _db.OutreachProjectIndustries
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        _db.OutreachProjectIndustries.RemoveRange(industries);

        var roles = await _db.OutreachProjectTargetRoles
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        _db.OutreachProjectTargetRoles.RemoveRange(roles);

        var histories = await _db.OutreachContactHistories
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        foreach (var history in histories)
        {
            history.ProjectId = null;
        }
    }

    private async Task<OutreachProjectSummaryDto> MapSummaryAsync(
        OutreachProject project,
        CancellationToken cancellationToken)
    {
        var prospects = await _db.Prospects.AsNoTracking()
            .Where(x => x.ProjectId == project.Id)
            .Select(x => new
            {
                x.Status,
                x.IsQualified,
                x.Email,
                x.WhatsApp,
                x.OutreachCampaignId,
                x.DuplicateKind
            })
            .ToListAsync(cancellationToken);

        var researchDates = await _db.ResearchRuns.AsNoTracking()
            .Where(x => x.ProjectId == project.Id)
            .Select(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        DateTimeOffset? lastResearch = researchDates.Count == 0
            ? null
            : researchDates.Max();

        var countries = project.Countries?.Select(x => x.Name).ToList()
            ?? await _db.OutreachProjectCountries.AsNoTracking()
                .Where(x => x.ProjectId == project.Id)
                .Select(x => x.Name)
                .ToListAsync(cancellationToken);

        var keywords = project.Keywords?.Where(x => !x.IsExcluded).Select(x => x.Value).ToList()
            ?? await _db.OutreachProjectKeywords.AsNoTracking()
                .Where(x => x.ProjectId == project.Id && !x.IsExcluded)
                .Select(x => x.Value)
                .ToListAsync(cancellationToken);

        return new OutreachProjectSummaryDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            ResearchInstructions = project.ResearchInstructions,
            IsActive = project.IsActive,
            EnableEmail = project.EnableEmail,
            EnableWhatsApp = project.EnableWhatsApp,
            Countries = countries,
            Keywords = keywords,
            ProspectCount = prospects.Count,
            QualifiedCount = prospects.Count(x => x.IsQualified || x.Status == ProspectStatus.Qualified),
            NeedsReviewCount = prospects.Count(x => x.Status == ProspectStatus.NeedsReview),
            RejectedCount = prospects.Count(x => x.Status == ProspectStatus.Rejected),
            ValidEmailCount = prospects.Count(x => !string.IsNullOrWhiteSpace(x.Email)),
            ValidWhatsAppCount = prospects.Count(x => !string.IsNullOrWhiteSpace(x.WhatsApp)),
            AlreadyContactedCount = prospects.Count(x =>
                x.Status == ProspectStatus.Contacted
                || x.DuplicateKind == ProspectDuplicateKind.EmailContacted
                || x.DuplicateKind == ProspectDuplicateKind.WhatsAppContacted),
            ReadyForOutreachCount = prospects.Count(x => x.Status == ProspectStatus.ReadyForOutreach),
            CampaignsCreatedCount = prospects.Count(x => x.OutreachCampaignId != null),
            LastResearchAtUtc = lastResearch,
            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc
        };
    }

    private async Task<List<ResearchRunSummaryDto>> LoadRecentRunsAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var runs = await _db.ResearchRuns.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        runs = runs.OrderByDescending(x => x.CreatedAtUtc).Take(5).ToList();

        var result = new List<ResearchRunSummaryDto>();
        foreach (var run in runs)
        {
            result.Add(ResearchRunMapper.MapSummary(run, null));
        }

        return result;
    }

    private static List<ProjectTemplateLinkDto> MapTemplates(IEnumerable<ProjectTemplate> templates)
    {
        var result = new List<ProjectTemplateLinkDto>();
        foreach (var item in templates.OrderBy(x => x.SortOrder))
        {
            result.Add(new ProjectTemplateLinkDto
            {
                Id = item.Id,
                TemplateId = item.TemplateId,
                TemplateName = item.Template?.Name,
                Channel = item.Channel,
                MatchIndustry = item.MatchIndustry,
                MatchCompanySize = item.MatchCompanySize,
                MatchNotes = item.MatchNotes,
                SortOrder = item.SortOrder
            });
        }

        return result;
    }

    private async Task EnsureTemplatesExistAsync(
        List<ProjectTemplateLinkDto> templates,
        CancellationToken cancellationToken)
    {
        foreach (var link in templates)
        {
            var exists = await _db.EmailTemplates.AsNoTracking()
                .AnyAsync(x => x.Id == link.TemplateId, cancellationToken);
            if (!exists)
            {
                throw new InvalidOperationException($"Template '{link.TemplateId}' was not found.");
            }
        }
    }

    private static void ValidateRequest(UpsertOutreachProjectDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Project name is required.");
        }

        if (!request.EnableEmail && !request.EnableWhatsApp)
        {
            throw new InvalidOperationException("At least one contact method must be enabled.");
        }
    }

    private sealed record ProjectCollectionLoadOptions(
        bool IncludeTemplates,
        bool IncludeIndustriesAndRoles);

    private async Task LoadProjectCollectionsAsync(
        OutreachProject project,
        ProjectCollectionLoadOptions options,
        CancellationToken cancellationToken)
    {
        project.Keywords = await _db.OutreachProjectKeywords.AsNoTracking()
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.Countries = await _db.OutreachProjectCountries.AsNoTracking()
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        if (options.IncludeIndustriesAndRoles)
        {
            project.Industries = await _db.OutreachProjectIndustries.AsNoTracking()
                .Where(x => x.ProjectId == project.Id)
                .ToListAsync(cancellationToken);
            project.TargetRoles = await _db.OutreachProjectTargetRoles.AsNoTracking()
                .Where(x => x.ProjectId == project.Id)
                .ToListAsync(cancellationToken);
        }

        if (options.IncludeTemplates)
        {
            project.Templates = await _db.ProjectTemplates.AsNoTracking()
                .Include(x => x.Template)
                .Where(x => x.ProjectId == project.Id)
                .ToListAsync(cancellationToken);
        }
    }

    private async Task ReplaceProjectCollectionsAsync(
        OutreachProject project,
        UpsertOutreachProjectDto request,
        CancellationToken cancellationToken)
    {
        await _db.OutreachProjectKeywords
            .Where(x => x.ProjectId == project.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await _db.OutreachProjectCountries
            .Where(x => x.ProjectId == project.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await _db.OutreachProjectIndustries
            .Where(x => x.ProjectId == project.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await _db.OutreachProjectTargetRoles
            .Where(x => x.ProjectId == project.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await _db.ProjectTemplates
            .Where(x => x.ProjectId == project.Id)
            .ExecuteDeleteAsync(cancellationToken);

        AddProjectCollections(project.Id, request);
    }

    private static void ApplyCollections(OutreachProject project, UpsertOutreachProjectDto request)
    {
        foreach (var value in DistinctValues(request.Keywords))
        {
            project.Keywords.Add(new OutreachProjectKeyword { ProjectId = project.Id, Value = value, IsExcluded = false });
        }

        foreach (var value in DistinctValues(request.ExcludedKeywords))
        {
            project.Keywords.Add(new OutreachProjectKeyword { ProjectId = project.Id, Value = value, IsExcluded = true });
        }

        foreach (var value in DistinctValues(request.Countries))
        {
            project.Countries.Add(new OutreachProjectCountry { ProjectId = project.Id, Name = value });
        }

        foreach (var value in DistinctValues(request.Industries))
        {
            project.Industries.Add(new OutreachProjectIndustry { ProjectId = project.Id, Value = value, IsExcluded = false });
        }

        foreach (var value in DistinctValues(request.ExcludedIndustries))
        {
            project.Industries.Add(new OutreachProjectIndustry { ProjectId = project.Id, Value = value, IsExcluded = true });
        }

        foreach (var value in DistinctValues(request.TargetRoles))
        {
            project.TargetRoles.Add(new OutreachProjectTargetRole { ProjectId = project.Id, Role = value });
        }

        var order = 0;
        foreach (var link in request.Templates)
        {
            project.Templates.Add(new ProjectTemplate
            {
                ProjectId = project.Id,
                TemplateId = link.TemplateId,
                Channel = link.Channel,
                MatchIndustry = TrimOrNull(link.MatchIndustry),
                MatchCompanySize = TrimOrNull(link.MatchCompanySize),
                MatchNotes = TrimOrNull(link.MatchNotes),
                SortOrder = link.SortOrder != 0 ? link.SortOrder : order
            });
            order++;
        }
    }

    private void AddProjectCollections(Guid projectId, UpsertOutreachProjectDto request)
    {
        foreach (var value in DistinctValues(request.Keywords))
        {
            _db.OutreachProjectKeywords.Add(new OutreachProjectKeyword
            {
                ProjectId = projectId,
                Value = value,
                IsExcluded = false
            });
        }

        foreach (var value in DistinctValues(request.ExcludedKeywords))
        {
            _db.OutreachProjectKeywords.Add(new OutreachProjectKeyword
            {
                ProjectId = projectId,
                Value = value,
                IsExcluded = true
            });
        }

        foreach (var value in DistinctValues(request.Countries))
        {
            _db.OutreachProjectCountries.Add(new OutreachProjectCountry
            {
                ProjectId = projectId,
                Name = value
            });
        }

        foreach (var value in DistinctValues(request.Industries))
        {
            _db.OutreachProjectIndustries.Add(new OutreachProjectIndustry
            {
                ProjectId = projectId,
                Value = value,
                IsExcluded = false
            });
        }

        foreach (var value in DistinctValues(request.ExcludedIndustries))
        {
            _db.OutreachProjectIndustries.Add(new OutreachProjectIndustry
            {
                ProjectId = projectId,
                Value = value,
                IsExcluded = true
            });
        }

        foreach (var value in DistinctValues(request.TargetRoles))
        {
            _db.OutreachProjectTargetRoles.Add(new OutreachProjectTargetRole
            {
                ProjectId = projectId,
                Role = value
            });
        }

        var order = 0;
        foreach (var link in request.Templates)
        {
            _db.ProjectTemplates.Add(new ProjectTemplate
            {
                ProjectId = projectId,
                TemplateId = link.TemplateId,
                Channel = link.Channel,
                MatchIndustry = TrimOrNull(link.MatchIndustry),
                MatchCompanySize = TrimOrNull(link.MatchCompanySize),
                MatchNotes = TrimOrNull(link.MatchNotes),
                SortOrder = link.SortOrder != 0 ? link.SortOrder : order
            });
            order++;
        }
    }

    private static List<string> DistinctValues(IEnumerable<string> values)
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

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
