using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services;

public class CampaignProcessor
{
    private readonly ISendlyDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IAttachmentStore _attachmentStore;
    private readonly ICampaignProgressNotifier _progressNotifier;
    private readonly SmtpSettingsService _smtpSettingsService;
    private readonly CampaignCancellationRegistry _cancellationRegistry;
    private readonly ILogger<CampaignProcessor> _logger;

    public CampaignProcessor(
        ISendlyDbContext db,
        IEmailSender emailSender,
        IAttachmentStore attachmentStore,
        ICampaignProgressNotifier progressNotifier,
        SmtpSettingsService smtpSettingsService,
        CampaignCancellationRegistry cancellationRegistry,
        ILogger<CampaignProcessor> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _attachmentStore = attachmentStore;
        _progressNotifier = progressNotifier;
        _smtpSettingsService = smtpSettingsService;
        _cancellationRegistry = cancellationRegistry;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid campaignId, CancellationToken stoppingToken)
    {
        var campaignToken = _cancellationRegistry.GetOrCreate(campaignId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, campaignToken);

        var campaign = await LoadCampaignAsync(campaignId, linked.Token);
        if (campaign is null)
        {
            return;
        }

        var credentials = await _smtpSettingsService.GetResolvedCredentialsAsync(linked.Token);
        if (credentials is null)
        {
            await FailCampaignAsync(campaign, "SMTP configuration is incomplete.", linked.Token);
            return;
        }

        var attachments = await LoadAttachmentsAsync(campaign, linked.Token);
        campaign.Status = CampaignStatus.Running;
        campaign.StartedAtUtc ??= DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(linked.Token);
        await NotifyAsync(campaign, null, linked.Token);

        var processedInBatch = 0;
        foreach (var recipient in GetPendingRecipients(campaign.Recipients))
        {
            if (linked.IsCancellationRequested || campaign.Status == CampaignStatus.Cancelled)
            {
                await CancelRemainingAsync(campaign, linked.Token);
                break;
            }

            await ProcessRecipientAsync(campaign, recipient, credentials, attachments, linked.Token);
            processedInBatch++;

            if (processedInBatch >= campaign.BatchSize)
            {
                processedInBatch = 0;
            }

            if (campaign.DelayMilliseconds > 0 && !linked.IsCancellationRequested)
            {
                await DelaySafeAsync(campaign.DelayMilliseconds, linked.Token);
            }
        }

        FinalizeCampaign(campaign);
        campaign.CurrentRecipientEmail = null;
        campaign.CompletedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
        await NotifyAsync(campaign, null, CancellationToken.None);
        _cancellationRegistry.Clear(campaignId);
    }

    private async Task ProcessRecipientAsync(
        Campaign campaign,
        CampaignRecipient recipient,
        ResolvedSmtpCredentials credentials,
        IReadOnlyList<EmailAttachmentContent> attachments,
        CancellationToken cancellationToken)
    {
        recipient.Status = RecipientDeliveryStatus.Sending;
        campaign.CurrentRecipientEmail = recipient.Email;
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(campaign, recipient, cancellationToken);

        var maxAttempts = Math.Max(1, campaign.MaxRetryCount + 1);
        EmailSendResult? lastResult = null;

        while (recipient.AttemptCount < maxAttempts)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                recipient.Status = RecipientDeliveryStatus.Cancelled;
                recipient.FailureReason = "Cancelled before send completed.";
                RecalculateCounts(campaign);
                await _db.SaveChangesAsync(CancellationToken.None);
                await NotifyAsync(campaign, recipient, CancellationToken.None);
                return;
            }

            recipient.AttemptCount++;
            recipient.LastAttemptAtUtc = DateTimeOffset.UtcNow;

            lastResult = await _emailSender.SendAsync(new EmailSendRequest
            {
                ToEmail = recipient.Email,
                Subject = campaign.Subject,
                HtmlBody = campaign.HtmlBody,
                PlainTextBody = campaign.PlainTextBody,
                Host = credentials.Host,
                Port = credentials.Port,
                SecurityMode = credentials.SecurityMode,
                Username = credentials.Username,
                Password = credentials.Password,
                SenderEmail = credentials.SenderEmail,
                SenderDisplayName = credentials.SenderDisplayName,
                ReplyToEmail = credentials.ReplyToEmail,
                Attachments = attachments
            }, cancellationToken);

            if (lastResult.Success)
            {
                recipient.Status = RecipientDeliveryStatus.Sent;
                recipient.FailureReason = null;
                recipient.IsTransientFailure = false;
                RecalculateCounts(campaign);
                await _db.SaveChangesAsync(cancellationToken);
                await NotifyAsync(campaign, recipient, cancellationToken);
                return;
            }

            if (!lastResult.IsTransientFailure)
            {
                break;
            }
        }

        recipient.Status = RecipientDeliveryStatus.Failed;
        recipient.FailureReason = SanitizeError(lastResult?.Message);
        recipient.IsTransientFailure = lastResult?.IsTransientFailure ?? false;
        RecalculateCounts(campaign);
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(campaign, recipient, cancellationToken);
        _logger.LogWarning("Failed to send campaign {CampaignId} to recipient {RecipientId}", campaign.Id, recipient.Id);
    }

    private async Task CancelRemainingAsync(Campaign campaign, CancellationToken cancellationToken)
    {
        foreach (var recipient in campaign.Recipients)
        {
            if (recipient.Status is RecipientDeliveryStatus.Pending or RecipientDeliveryStatus.Sending)
            {
                recipient.Status = RecipientDeliveryStatus.Cancelled;
                recipient.FailureReason = "Cancelled by user.";
            }
        }

        campaign.Status = CampaignStatus.Cancelled;
        RecalculateCounts(campaign);
        await _db.SaveChangesAsync(CancellationToken.None);
        await NotifyAsync(campaign, null, CancellationToken.None);
    }

    private async Task FailCampaignAsync(Campaign campaign, string reason, CancellationToken cancellationToken)
    {
        campaign.Status = CampaignStatus.Failed;
        campaign.CompletedAtUtc = DateTimeOffset.UtcNow;
        foreach (var recipient in campaign.Recipients)
        {
            if (recipient.Status == RecipientDeliveryStatus.Pending)
            {
                recipient.Status = RecipientDeliveryStatus.Failed;
                recipient.FailureReason = reason;
            }
        }

        RecalculateCounts(campaign);
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(campaign, null, cancellationToken);
        _cancellationRegistry.Clear(campaign.Id);
    }

    private async Task<Campaign?> LoadCampaignAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        return await _db.Campaigns
            .Include(x => x.Recipients)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == campaignId, cancellationToken);
    }

    private async Task<IReadOnlyList<EmailAttachmentContent>> LoadAttachmentsAsync(
        Campaign campaign,
        CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        foreach (var attachment in campaign.Attachments)
        {
            ids.Add(attachment.Id);
        }

        return await _attachmentStore.LoadAsync(ids, cancellationToken);
    }

    private static List<CampaignRecipient> GetPendingRecipients(ICollection<CampaignRecipient> recipients)
    {
        var result = new List<CampaignRecipient>();
        foreach (var recipient in recipients)
        {
            if (recipient.Status == RecipientDeliveryStatus.Pending)
            {
                result.Add(recipient);
            }
        }

        return result;
    }

    private static void FinalizeCampaign(Campaign campaign)
    {
        RecalculateCounts(campaign);

        if (campaign.Status == CampaignStatus.Cancelled)
        {
            return;
        }

        if (campaign.FailedCount > 0 && campaign.SuccessCount > 0)
        {
            campaign.Status = CampaignStatus.CompletedWithFailures;
            return;
        }

        if (campaign.FailedCount > 0 && campaign.SuccessCount == 0)
        {
            campaign.Status = CampaignStatus.Failed;
            return;
        }

        campaign.Status = CampaignStatus.Completed;
    }

    private static void RecalculateCounts(Campaign campaign)
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

    private async Task NotifyAsync(Campaign campaign, CampaignRecipient? recipient, CancellationToken cancellationToken)
    {
        var progress = new CampaignProgressDto
        {
            CampaignId = campaign.Id,
            Status = campaign.Status,
            TotalRecipients = campaign.TotalRecipients,
            SuccessCount = campaign.SuccessCount,
            FailedCount = campaign.FailedCount,
            CancelledCount = campaign.CancelledCount,
            PendingCount = campaign.PendingCount,
            ProgressPercent = CalculateProgress(campaign),
            CurrentRecipientEmail = campaign.CurrentRecipientEmail,
            StartedAtUtc = campaign.StartedAtUtc,
            CompletedAtUtc = campaign.CompletedAtUtc
        };

        if (recipient is not null)
        {
            progress.UpdatedRecipient = new CampaignRecipientDto
            {
                Id = recipient.Id,
                Email = recipient.Email,
                Status = recipient.Status,
                AttemptCount = recipient.AttemptCount,
                LastAttemptAtUtc = recipient.LastAttemptAtUtc,
                FailureReason = recipient.FailureReason
            };
        }

        await _progressNotifier.NotifyProgressAsync(progress, cancellationToken);
    }

    private static int CalculateProgress(Campaign campaign)
    {
        if (campaign.TotalRecipients <= 0)
        {
            return 0;
        }

        var completed = campaign.SuccessCount + campaign.FailedCount + campaign.CancelledCount;
        return (int)Math.Round(completed * 100d / campaign.TotalRecipients);
    }

    private static async Task DelaySafeAsync(int milliseconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(milliseconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string SanitizeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "SMTP send failed.";
        }

        var sanitized = message.Replace("password", "****", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length > 500 ? sanitized[..500] : sanitized;
    }
}
