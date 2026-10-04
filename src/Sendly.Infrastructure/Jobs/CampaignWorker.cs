using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sendly.Infrastructure.Jobs;

public class CampaignWorker : BackgroundService
{
    private readonly ICampaignQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CampaignWorker> _logger;

    public CampaignWorker(
        ICampaignQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<CampaignWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedCampaignsAsync(stoppingToken);

        await foreach (var campaignId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<CampaignProcessor>();
                await processor.ProcessAsync(campaignId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Campaign worker failed for {CampaignId}", campaignId);
            }
        }
    }

    private async Task RecoverInterruptedCampaignsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISendlyDbContext>();
        var campaigns = await db.Campaigns
            .Where(x => x.Status == CampaignStatus.Queued || x.Status == CampaignStatus.Running)
            .Select(x => x.Id)
            .ToListAsync(stoppingToken);

        foreach (var campaignId in campaigns)
        {
            await _queue.EnqueueAsync(campaignId, stoppingToken);
        }
    }
}
