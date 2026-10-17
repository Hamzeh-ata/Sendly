using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services;

public class SequenceProcessor
{
    private readonly ISendlyDbContext _db;
    private readonly IEnumerable<IOutreachChannel> _channels;
    private readonly ISequenceQueue _queue;
    private readonly CampaignService _campaignService;
    private readonly ILogger<SequenceProcessor> _logger;

    public SequenceProcessor(
        ISendlyDbContext db,
        IEnumerable<IOutreachChannel> channels,
        ISequenceQueue queue,
        CampaignService campaignService,
        ILogger<SequenceProcessor> logger)
    {
        _db = db;
        _channels = channels;
        _queue = queue;
        _campaignService = campaignService;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var enrollment = await _db.ProspectSequenceEnrollments
            .FirstOrDefaultAsync(x => x.Id == enrollmentId, cancellationToken);
        if (enrollment is null)
        {
            return;
        }

        if (enrollment.Status is SequenceEnrollmentStatus.Completed
            or SequenceEnrollmentStatus.Cancelled
            or SequenceEnrollmentStatus.Failed)
        {
            return;
        }

        if (enrollment.NextRunAtUtc is not null && enrollment.NextRunAtUtc > DateTimeOffset.UtcNow.AddSeconds(5))
        {
            enrollment.Status = SequenceEnrollmentStatus.Waiting;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var sequence = await _db.OutreachSequences
            .Include(x => x.Steps)
            .ThenInclude(x => x.Template)
            .FirstOrDefaultAsync(x => x.Id == enrollment.SequenceId, cancellationToken);
        if (sequence is null || !sequence.IsActive)
        {
            enrollment.Status = SequenceEnrollmentStatus.Failed;
            enrollment.LastError = "Sequence was not found or is inactive.";
            enrollment.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var steps = sequence.Steps.OrderBy(x => x.StepOrder).ToList();
        var step = ResolveStep(steps, enrollment.CurrentStepOrder, enrollment.AssignedVariantKey);
        if (step is null)
        {
            enrollment.Status = SequenceEnrollmentStatus.Completed;
            enrollment.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        enrollment.Status = SequenceEnrollmentStatus.Running;
        enrollment.LastError = null;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var channel = ResolveChannel(step.Channel);
            var prepare = await channel.PrepareAsync(
                new OutreachPrepareRequest
                {
                    ProjectId = enrollment.ProjectId,
                    ProspectIds = [enrollment.ProspectId],
                    Channel = step.Channel,
                    TemplateId = step.TemplateId,
                    UseAiRecommendedTemplate = step.TemplateId is null,
                    AllowAlreadyContacted = false
                },
                cancellationToken);

            if (prepare.PreparedCount == 0)
            {
                enrollment.Status = SequenceEnrollmentStatus.Failed;
                enrollment.LastError = prepare.Warnings.Count > 0
                    ? string.Join("; ", prepare.Warnings)
                    : "Step prepared zero prospects.";
                enrollment.CompletedAtUtc = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            if (prepare.CampaignId is not null)
            {
                await _campaignService.StartAsync(prepare.CampaignId.Value, cancellationToken);
            }

            var nextOrder = steps
                .Where(x => x.StepOrder > step.StepOrder)
                .Select(x => x.StepOrder)
                .DefaultIfEmpty()
                .Min();
            if (nextOrder == 0 || nextOrder <= step.StepOrder)
            {
                enrollment.Status = SequenceEnrollmentStatus.Completed;
                enrollment.CompletedAtUtc = DateTimeOffset.UtcNow;
                enrollment.NextRunAtUtc = null;
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Sequence enrollment completed {EnrollmentId}", enrollmentId);
                return;
            }

            var next = ResolveStep(steps, nextOrder, enrollment.AssignedVariantKey);
            if (next is null)
            {
                enrollment.Status = SequenceEnrollmentStatus.Completed;
                enrollment.CompletedAtUtc = DateTimeOffset.UtcNow;
                enrollment.NextRunAtUtc = null;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            enrollment.CurrentStepOrder = next.StepOrder;
            enrollment.Status = SequenceEnrollmentStatus.Waiting;
            enrollment.NextRunAtUtc = DateTimeOffset.UtcNow.AddHours(Math.Max(0, next.DelayHoursAfterPrevious));
            await _db.SaveChangesAsync(cancellationToken);

            if (enrollment.NextRunAtUtc <= DateTimeOffset.UtcNow.AddSeconds(5))
            {
                await _queue.EnqueueAsync(enrollment.Id, cancellationToken);
            }

            _logger.LogInformation(
                "Sequence enrollment {EnrollmentId} advanced to step {Step}",
                enrollmentId,
                next.StepOrder);
        }
        catch (Exception ex)
        {
            enrollment.Status = SequenceEnrollmentStatus.Failed;
            enrollment.LastError = ex.Message;
            enrollment.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Sequence enrollment failed {EnrollmentId}", enrollmentId);
        }
    }

    private static OutreachSequenceStep? ResolveStep(
        List<OutreachSequenceStep> steps,
        int stepOrder,
        string? assignedVariantKey)
    {
        var atOrder = steps.Where(x => x.StepOrder == stepOrder).ToList();
        if (atOrder.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(assignedVariantKey))
        {
            var match = atOrder.FirstOrDefault(x =>
                string.Equals(x.VariantKey, assignedVariantKey, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        var defaultStep = atOrder.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.VariantKey));
        return defaultStep ?? atOrder[0];
    }

    private IOutreachChannel ResolveChannel(OutreachChannelType channel)
    {
        foreach (var item in _channels)
        {
            if (item.Channel == channel)
            {
                return item;
            }
        }

        throw new InvalidOperationException($"Outreach channel '{channel}' is not available.");
    }
}
