using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services;

public class OutreachSequenceService
{
    private readonly ISendlyDbContext _db;
    private readonly ISequenceQueue _queue;
    private readonly ILogger<OutreachSequenceService> _logger;

    public OutreachSequenceService(
        ISendlyDbContext db,
        ISequenceQueue queue,
        ILogger<OutreachSequenceService> logger)
    {
        _db = db;
        _queue = queue;
        _logger = logger;
    }

    public async Task<List<OutreachSequenceSummaryDto>> ListAsync(
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var query = _db.OutreachSequences.AsNoTracking().AsQueryable();
        if (projectId is not null)
        {
            query = query.Where(x => x.ProjectId == projectId);
        }

        var sequences = await query
            .Include(x => x.Steps)
            .ToListAsync(cancellationToken);
        sequences = sequences.OrderByDescending(x => x.UpdatedAtUtc).ToList();

        var projectIds = sequences.Select(x => x.ProjectId).Distinct().ToList();
        var names = await _db.OutreachProjects.AsNoTracking()
            .Where(x => projectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var result = new List<OutreachSequenceSummaryDto>();
        foreach (var sequence in sequences)
        {
            names.TryGetValue(sequence.ProjectId, out var projectName);
            var activeCount = await _db.ProspectSequenceEnrollments.AsNoTracking()
                .CountAsync(
                    x => x.SequenceId == sequence.Id
                         && (x.Status == SequenceEnrollmentStatus.Pending
                             || x.Status == SequenceEnrollmentStatus.Running
                             || x.Status == SequenceEnrollmentStatus.Waiting),
                    cancellationToken);
            result.Add(MapSummary(sequence, projectName, activeCount));
        }

        return result;
    }

    public async Task<OutreachSequenceDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var sequence = await _db.OutreachSequences.AsNoTracking()
            .Include(x => x.Steps)
            .ThenInclude(x => x.Template)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (sequence is null)
        {
            return null;
        }

        var projectName = await _db.OutreachProjects.AsNoTracking()
            .Where(x => x.Id == sequence.ProjectId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);
        var activeCount = await _db.ProspectSequenceEnrollments.AsNoTracking()
            .CountAsync(
                x => x.SequenceId == sequence.Id
                     && (x.Status == SequenceEnrollmentStatus.Pending
                         || x.Status == SequenceEnrollmentStatus.Running
                         || x.Status == SequenceEnrollmentStatus.Waiting),
                cancellationToken);

        var detail = new OutreachSequenceDetailDto
        {
            Id = sequence.Id,
            ProjectId = sequence.ProjectId,
            ProjectName = projectName,
            Name = sequence.Name,
            Description = sequence.Description,
            IsActive = sequence.IsActive,
            StepCount = sequence.Steps.Count,
            ActiveEnrollmentCount = activeCount,
            CreatedAtUtc = sequence.CreatedAtUtc,
            UpdatedAtUtc = sequence.UpdatedAtUtc,
            Steps = MapSteps(sequence.Steps)
        };
        return detail;
    }

    public async Task<OutreachSequenceDetailDto> CreateAsync(
        Guid projectId,
        UpsertOutreachSequenceDto request,
        CancellationToken cancellationToken)
    {
        await EnsureProjectExistsAsync(projectId, cancellationToken);
        ValidateRequest(request);
        await EnsureTemplatesExistAsync(request.Steps, cancellationToken);

        var sequence = new OutreachSequence
        {
            ProjectId = projectId,
            Name = request.Name.Trim(),
            Description = TrimOrNull(request.Description),
            IsActive = request.IsActive
        };
        ApplySteps(sequence, request.Steps);
        _db.OutreachSequences.Add(sequence);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Outreach sequence created {SequenceId}", sequence.Id);
        return (await GetAsync(sequence.Id, cancellationToken))!;
    }

    public async Task<OutreachSequenceDetailDto> UpdateAsync(
        Guid id,
        UpsertOutreachSequenceDto request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        await EnsureTemplatesExistAsync(request.Steps, cancellationToken);

        var sequence = await _db.OutreachSequences
            .Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (sequence is null)
        {
            throw new InvalidOperationException("Sequence was not found.");
        }

        sequence.Name = request.Name.Trim();
        sequence.Description = TrimOrNull(request.Description);
        sequence.IsActive = request.IsActive;
        sequence.UpdatedAtUtc = DateTimeOffset.UtcNow;
        sequence.Steps.Clear();
        ApplySteps(sequence, request.Steps);
        await _db.SaveChangesAsync(cancellationToken);
        return (await GetAsync(sequence.Id, cancellationToken))!;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var sequence = await _db.OutreachSequences.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (sequence is null)
        {
            throw new InvalidOperationException("Sequence was not found.");
        }

        var hasActive = await _db.ProspectSequenceEnrollments.AnyAsync(
            x => x.SequenceId == id
                 && (x.Status == SequenceEnrollmentStatus.Pending
                     || x.Status == SequenceEnrollmentStatus.Running
                     || x.Status == SequenceEnrollmentStatus.Waiting),
            cancellationToken);
        if (hasActive)
        {
            sequence.IsActive = false;
            sequence.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        _db.OutreachSequences.Remove(sequence);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<SequenceEnrollmentDto>> EnrollAsync(
        Guid sequenceId,
        EnrollProspectsRequestDto request,
        CancellationToken cancellationToken)
    {
        var sequence = await _db.OutreachSequences
            .Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == sequenceId, cancellationToken);
        if (sequence is null)
        {
            throw new InvalidOperationException("Sequence was not found.");
        }

        if (!sequence.IsActive)
        {
            throw new InvalidOperationException("Sequence is inactive.");
        }

        if (sequence.Steps.Count == 0)
        {
            throw new InvalidOperationException("Sequence has no steps.");
        }

        if (request.ProspectIds.Count == 0)
        {
            throw new InvalidOperationException("Select at least one prospect.");
        }

        var firstOrder = sequence.Steps.Min(x => x.StepOrder);
        var firstStepVariants = sequence.Steps
            .Where(x => x.StepOrder == firstOrder)
            .ToList();
        var firstStep = ResolveEnrollmentStep(firstStepVariants, null);
        var prospects = await _db.Prospects
            .Where(x => x.ProjectId == sequence.ProjectId && request.ProspectIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var created = new List<ProspectSequenceEnrollment>();
        foreach (var prospect in prospects)
        {
            if (prospect.Status == ProspectStatus.DoNotContact)
            {
                continue;
            }

            var exists = await _db.ProspectSequenceEnrollments.AnyAsync(
                x => x.SequenceId == sequenceId
                     && x.ProspectId == prospect.Id
                     && x.Status != SequenceEnrollmentStatus.Cancelled
                     && x.Status != SequenceEnrollmentStatus.Completed
                     && x.Status != SequenceEnrollmentStatus.Failed,
                cancellationToken);
            if (exists)
            {
                continue;
            }

            var assignedVariant = AssignVariant(firstStepVariants);
            var startStep = ResolveEnrollmentStep(firstStepVariants, assignedVariant) ?? firstStep;
            var enrollment = new ProspectSequenceEnrollment
            {
                SequenceId = sequenceId,
                ProspectId = prospect.Id,
                ProjectId = sequence.ProjectId,
                Status = SequenceEnrollmentStatus.Pending,
                CurrentStepOrder = startStep!.StepOrder,
                AssignedVariantKey = assignedVariant,
                NextRunAtUtc = DateTimeOffset.UtcNow.AddHours(Math.Max(0, startStep.DelayHoursAfterPrevious)),
                EnrolledAtUtc = DateTimeOffset.UtcNow
            };
            _db.ProspectSequenceEnrollments.Add(enrollment);
            created.Add(enrollment);
        }

        if (created.Count == 0)
        {
            throw new InvalidOperationException("No new prospects were enrolled.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        foreach (var enrollment in created)
        {
            await _queue.EnqueueAsync(enrollment.Id, cancellationToken);
        }

        _logger.LogInformation(
            "Enrolled {Count} prospects into sequence {SequenceId}",
            created.Count,
            sequenceId);

        return await ListEnrollmentsAsync(sequenceId, cancellationToken);
    }

    public async Task<List<SequenceEnrollmentDto>> ListEnrollmentsAsync(
        Guid sequenceId,
        CancellationToken cancellationToken)
    {
        var enrollments = await _db.ProspectSequenceEnrollments.AsNoTracking()
            .Where(x => x.SequenceId == sequenceId)
            .ToListAsync(cancellationToken);
        enrollments = enrollments.OrderByDescending(x => x.EnrolledAtUtc).ToList();

        var sequenceName = await _db.OutreachSequences.AsNoTracking()
            .Where(x => x.Id == sequenceId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var prospectIds = enrollments.Select(x => x.ProspectId).Distinct().ToList();
        var companies = await _db.Prospects.AsNoTracking()
            .Where(x => prospectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.CompanyName, cancellationToken);

        var result = new List<SequenceEnrollmentDto>();
        foreach (var item in enrollments)
        {
            companies.TryGetValue(item.ProspectId, out var company);
            result.Add(new SequenceEnrollmentDto
            {
                Id = item.Id,
                SequenceId = item.SequenceId,
                SequenceName = sequenceName,
                ProspectId = item.ProspectId,
                ProspectCompanyName = company,
                ProjectId = item.ProjectId,
                Status = item.Status,
                CurrentStepOrder = item.CurrentStepOrder,
                AssignedVariantKey = item.AssignedVariantKey,
                NextRunAtUtc = item.NextRunAtUtc,
                EnrolledAtUtc = item.EnrolledAtUtc,
                CompletedAtUtc = item.CompletedAtUtc,
                LastError = item.LastError
            });
        }

        return result;
    }

    public async Task CancelEnrollmentAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var enrollment = await _db.ProspectSequenceEnrollments
            .FirstOrDefaultAsync(x => x.Id == enrollmentId, cancellationToken);
        if (enrollment is null)
        {
            throw new InvalidOperationException("Enrollment was not found.");
        }

        if (enrollment.Status is SequenceEnrollmentStatus.Completed or SequenceEnrollmentStatus.Cancelled)
        {
            return;
        }

        enrollment.Status = SequenceEnrollmentStatus.Cancelled;
        enrollment.CompletedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SequenceAnalyticsDto?> GetAnalyticsAsync(Guid sequenceId, CancellationToken cancellationToken)
    {
        var sequence = await _db.OutreachSequences.AsNoTracking()
            .Include(x => x.Steps)
            .ThenInclude(x => x.Template)
            .FirstOrDefaultAsync(x => x.Id == sequenceId, cancellationToken);
        if (sequence is null)
        {
            return null;
        }

        var enrollments = await _db.ProspectSequenceEnrollments.AsNoTracking()
            .Where(x => x.SequenceId == sequenceId)
            .ToListAsync(cancellationToken);

        var activeCount = CountByStatuses(
            enrollments,
            SequenceEnrollmentStatus.Pending,
            SequenceEnrollmentStatus.Running,
            SequenceEnrollmentStatus.Waiting);
        var completedCount = CountByStatuses(enrollments, SequenceEnrollmentStatus.Completed);
        var failedCount = CountByStatuses(enrollments, SequenceEnrollmentStatus.Failed);
        var cancelledCount = CountByStatuses(enrollments, SequenceEnrollmentStatus.Cancelled);
        var total = enrollments.Count;

        return new SequenceAnalyticsDto
        {
            SequenceId = sequence.Id,
            SequenceName = sequence.Name,
            TotalEnrollments = total,
            ActiveCount = activeCount,
            CompletedCount = completedCount,
            FailedCount = failedCount,
            CancelledCount = cancelledCount,
            CompletionRate = ComputeRate(completedCount, total),
            Variants = BuildVariantAnalytics(enrollments),
            Steps = BuildStepAnalytics(sequence.Steps, enrollments)
        };
    }

    private static OutreachSequenceSummaryDto MapSummary(
        OutreachSequence sequence,
        string? projectName,
        int activeCount)
    {
        return new OutreachSequenceSummaryDto
        {
            Id = sequence.Id,
            ProjectId = sequence.ProjectId,
            ProjectName = projectName,
            Name = sequence.Name,
            Description = sequence.Description,
            IsActive = sequence.IsActive,
            StepCount = sequence.Steps.Count,
            ActiveEnrollmentCount = activeCount,
            CreatedAtUtc = sequence.CreatedAtUtc,
            UpdatedAtUtc = sequence.UpdatedAtUtc
        };
    }

    private static List<OutreachSequenceStepDto> MapSteps(IEnumerable<OutreachSequenceStep> steps)
    {
        var result = new List<OutreachSequenceStepDto>();
        foreach (var step in steps.OrderBy(x => x.StepOrder).ThenBy(x => x.VariantKey))
        {
            result.Add(new OutreachSequenceStepDto
            {
                Id = step.Id,
                StepOrder = step.StepOrder,
                Channel = step.Channel,
                TemplateId = step.TemplateId,
                TemplateName = step.Template?.Name,
                DelayHoursAfterPrevious = step.DelayHoursAfterPrevious,
                WhatsAppTemplateName = step.WhatsAppTemplateName,
                WhatsAppTemplateLanguage = step.WhatsAppTemplateLanguage,
                VariantKey = step.VariantKey,
                TrafficPercent = step.TrafficPercent
            });
        }

        return result;
    }

    private static void ApplySteps(OutreachSequence sequence, List<OutreachSequenceStepDto> steps)
    {
        var order = 1;
        foreach (var step in steps.OrderBy(x => x.StepOrder == 0 ? int.MaxValue : x.StepOrder))
        {
            sequence.Steps.Add(new OutreachSequenceStep
            {
                SequenceId = sequence.Id,
                StepOrder = step.StepOrder > 0 ? step.StepOrder : order,
                Channel = step.Channel,
                TemplateId = step.TemplateId,
                DelayHoursAfterPrevious = Math.Max(0, step.DelayHoursAfterPrevious),
                WhatsAppTemplateName = TrimOrNull(step.WhatsAppTemplateName),
                WhatsAppTemplateLanguage = TrimOrNull(step.WhatsAppTemplateLanguage),
                VariantKey = TrimOrNull(step.VariantKey),
                TrafficPercent = Math.Clamp(step.TrafficPercent <= 0 ? 100 : step.TrafficPercent, 1, 100)
            });
            order++;
        }
    }

    private static string? AssignVariant(List<OutreachSequenceStep> stepsAtOrder)
    {
        var variants = new List<(string VariantKey, int TrafficPercent)>();
        foreach (var step in stepsAtOrder)
        {
            if (string.IsNullOrWhiteSpace(step.VariantKey))
            {
                continue;
            }

            variants.Add((step.VariantKey.Trim(), step.TrafficPercent <= 0 ? 100 : step.TrafficPercent));
        }

        return Domain.Services.SequenceVariantAssigner.Assign(variants, Random.Shared.Next(100));
    }

    private static OutreachSequenceStep? ResolveEnrollmentStep(
        List<OutreachSequenceStep> stepsAtOrder,
        string? assignedVariant)
    {
        if (stepsAtOrder.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(assignedVariant))
        {
            var match = stepsAtOrder.FirstOrDefault(x =>
                string.Equals(x.VariantKey, assignedVariant, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return stepsAtOrder.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.VariantKey))
               ?? stepsAtOrder[0];
    }

    private static List<SequenceVariantAnalyticsDto> BuildVariantAnalytics(
        List<ProspectSequenceEnrollment> enrollments)
    {
        var groups = enrollments
            .GroupBy(x => string.IsNullOrWhiteSpace(x.AssignedVariantKey) ? "default" : x.AssignedVariantKey!.Trim())
            .OrderBy(x => x.Key)
            .ToList();

        var result = new List<SequenceVariantAnalyticsDto>();
        foreach (var group in groups)
        {
            var items = group.ToList();
            var completed = CountByStatuses(items, SequenceEnrollmentStatus.Completed);
            var failed = CountByStatuses(items, SequenceEnrollmentStatus.Failed);
            var active = CountByStatuses(
                items,
                SequenceEnrollmentStatus.Pending,
                SequenceEnrollmentStatus.Running,
                SequenceEnrollmentStatus.Waiting);
            result.Add(new SequenceVariantAnalyticsDto
            {
                VariantKey = group.Key,
                EnrollmentCount = items.Count,
                CompletedCount = completed,
                FailedCount = failed,
                ActiveCount = active,
                CompletionRate = ComputeRate(completed, items.Count)
            });
        }

        return result;
    }

    private static List<SequenceStepAnalyticsDto> BuildStepAnalytics(
        ICollection<OutreachSequenceStep> steps,
        List<ProspectSequenceEnrollment> enrollments)
    {
        var result = new List<SequenceStepAnalyticsDto>();
        foreach (var step in steps.OrderBy(x => x.StepOrder).ThenBy(x => x.VariantKey))
        {
            var reached = 0;
            foreach (var enrollment in enrollments)
            {
                if (!VariantMatches(enrollment.AssignedVariantKey, step.VariantKey))
                {
                    continue;
                }

                if (enrollment.Status == SequenceEnrollmentStatus.Completed
                    || enrollment.CurrentStepOrder > step.StepOrder
                    || (enrollment.CurrentStepOrder == step.StepOrder
                        && enrollment.Status is SequenceEnrollmentStatus.Running
                            or SequenceEnrollmentStatus.Waiting
                            or SequenceEnrollmentStatus.Failed))
                {
                    reached++;
                }
            }

            result.Add(new SequenceStepAnalyticsDto
            {
                StepOrder = step.StepOrder,
                VariantKey = step.VariantKey,
                Channel = step.Channel,
                TemplateName = step.Template?.Name,
                TrafficPercent = step.TrafficPercent,
                ReachedCount = reached
            });
        }

        return result;
    }

    private static bool VariantMatches(string? assigned, string? stepVariant)
    {
        if (string.IsNullOrWhiteSpace(stepVariant))
        {
            return true;
        }

        return string.Equals(assigned, stepVariant, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountByStatuses(
        List<ProspectSequenceEnrollment> enrollments,
        params SequenceEnrollmentStatus[] statuses)
    {
        var count = 0;
        foreach (var item in enrollments)
        {
            foreach (var status in statuses)
            {
                if (item.Status == status)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private static double ComputeRate(int completed, int total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return Math.Round(completed * 100.0 / total, 1);
    }

    private async Task EnsureProjectExistsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var exists = await _db.OutreachProjects.AsNoTracking().AnyAsync(x => x.Id == projectId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Project was not found.");
        }
    }

    private async Task EnsureTemplatesExistAsync(
        List<OutreachSequenceStepDto> steps,
        CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            if (step.TemplateId is null)
            {
                continue;
            }

            var exists = await _db.EmailTemplates.AsNoTracking()
                .AnyAsync(x => x.Id == step.TemplateId, cancellationToken);
            if (!exists)
            {
                throw new InvalidOperationException($"Template '{step.TemplateId}' was not found.");
            }
        }
    }

    private static void ValidateRequest(UpsertOutreachSequenceDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Sequence name is required.");
        }

        if (request.Steps.Count == 0)
        {
            throw new InvalidOperationException("At least one sequence step is required.");
        }
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
