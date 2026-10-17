using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
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
                await MarkCampaignFailedAsync(campaignId, ex.Message, stoppingToken);
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

    private async Task MarkCampaignFailedAsync(
        Guid campaignId,
        string? error,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISendlyDbContext>();
            var campaign = await db.Campaigns
                .Include(x => x.Recipients)
                .FirstOrDefaultAsync(x => x.Id == campaignId, cancellationToken);
            if (campaign is null)
            {
                return;
            }

            if (campaign.Status is CampaignStatus.Completed
                or CampaignStatus.CompletedWithFailures
                or CampaignStatus.Cancelled
                or CampaignStatus.Failed)
            {
                return;
            }

            var reason = string.IsNullOrWhiteSpace(error)
                ? "Campaign worker failed."
                : SanitizeWorkerError(error);
            campaign.Status = CampaignStatus.Failed;
            campaign.CompletedAtUtc = DateTimeOffset.UtcNow;
            campaign.CurrentRecipientEmail = null;
            foreach (var recipient in campaign.Recipients)
            {
                if (recipient.Status is RecipientDeliveryStatus.Pending or RecipientDeliveryStatus.Sending)
                {
                    recipient.Status = RecipientDeliveryStatus.Failed;
                    recipient.FailureReason = reason;
                }
            }

            Recount(campaign);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark campaign {CampaignId} as failed", campaignId);
        }
    }

    private static void Recount(Campaign campaign)
    {
        var success = 0;
        var failed = 0;
        var cancelled = 0;
        var pending = 0;
        foreach (var recipient in campaign.Recipients)
        {
            switch (recipient.Status)
            {
                case RecipientDeliveryStatus.Sent:
                    success++;
                    break;
                case RecipientDeliveryStatus.Failed:
                    failed++;
                    break;
                case RecipientDeliveryStatus.Cancelled:
                    cancelled++;
                    break;
                default:
                    pending++;
                    break;
            }
        }

        campaign.SuccessCount = success;
        campaign.FailedCount = failed;
        campaign.CancelledCount = cancelled;
        campaign.PendingCount = pending;
    }

    private static string SanitizeWorkerError(string message)
    {
        var sanitized = message.Replace("password", "****", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length > 500 ? sanitized[..500] : sanitized;
    }
}
