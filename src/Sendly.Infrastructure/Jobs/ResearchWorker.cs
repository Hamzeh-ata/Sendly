using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Application.Services.MapResearch;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sendly.Infrastructure.Jobs;

public class ResearchWorker : BackgroundService
{
    private readonly IResearchQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResearchWorker> _logger;

    public ResearchWorker(
        IResearchQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ResearchWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedRunsAsync(stoppingToken);

        await foreach (var researchRunId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ISendlyDbContext>();
                var runMode = await db.ResearchRuns
                    .Where(x => x.Id == researchRunId)
                    .Select(x => (ResearchMode?)x.Mode)
                    .FirstOrDefaultAsync(stoppingToken);
                if (runMode == ResearchMode.Map)
                {
                    var mapProcessor = scope.ServiceProvider.GetRequiredService<MapResearchProcessor>();
                    await mapProcessor.ProcessAsync(researchRunId, stoppingToken);
                }
                else
                {
                    var processor = scope.ServiceProvider.GetRequiredService<ResearchProcessor>();
                    await processor.ProcessAsync(researchRunId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Research worker failed for {ResearchRunId}", researchRunId);
            }
        }
    }

    private async Task RecoverInterruptedRunsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISendlyDbContext>();
        var runs = await db.ResearchRuns
            .Where(x => x.Status == ResearchRunStatus.Pending || x.Status == ResearchRunStatus.Running)
            .Select(x => x.Id)
            .ToListAsync(stoppingToken);

        foreach (var researchRunId in runs)
        {
            await _queue.EnqueueAsync(researchRunId, stoppingToken);
        }
    }
}
