using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sendly.Infrastructure.Jobs;

public class SequenceDueScanner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISequenceQueue _queue;
    private readonly ILogger<SequenceDueScanner> _logger;

    public SequenceDueScanner(
        IServiceScopeFactory scopeFactory,
        ISequenceQueue queue,
        ILogger<SequenceDueScanner> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sequence due scanner failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ScanDueAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISendlyDbContext>();
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.ProspectSequenceEnrollments
            .AsNoTracking()
            .Where(x =>
                x.Status == SequenceEnrollmentStatus.Waiting || x.Status == SequenceEnrollmentStatus.Pending)
            .Select(x => new { x.Id, x.NextRunAtUtc })
            .ToListAsync(cancellationToken);

        var dueIds = new List<Guid>();
        foreach (var item in candidates)
        {
            if (item.NextRunAtUtc is not null && item.NextRunAtUtc <= now)
            {
                dueIds.Add(item.Id);
                if (dueIds.Count >= 100)
                {
                    break;
                }
            }
        }

        foreach (var id in dueIds)
        {
            await _queue.EnqueueAsync(id, cancellationToken);
        }

        if (dueIds.Count > 0)
        {
            _logger.LogInformation("Sequence due scanner enqueued {Count} enrollments", dueIds.Count);
        }
    }
}
