using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services;

public class CampaignProcessor
{
    private readonly ISendlyDbContext _db;
    private readonly IEmailDeliveryService _emailDelivery;
    private readonly IAttachmentStore _attachmentStore;
    private readonly ICampaignProgressNotifier _progressNotifier;
    private readonly TemplateRecipientProtectionService _templateRecipientProtection;
    private readonly CampaignCancellationRegistry _cancellationRegistry;
    private readonly ILogger<CampaignProcessor> _logger;

    public CampaignProcessor(
        ISendlyDbContext db,
        IEmailDeliveryService emailDelivery,
        IAttachmentStore attachmentStore,
        ICampaignProgressNotifier progressNotifier,
        TemplateRecipientProtectionService templateRecipientProtection,
        CampaignCancellationRegistry cancellationRegistry,
        ILogger<CampaignProcessor> logger)
    {
        _db = db;
        _emailDelivery = emailDelivery;
        _attachmentStore = attachmentStore;
        _progressNotifier = progressNotifier;
        _templateRecipientProtection = templateRecipientProtection;
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

        if (!campaign.EmailAccountId.HasValue)
        {
            await FailCampaignAsync(campaign, "Sending account is missing for this campaign.", linked.Token);
            return;
        }

        var accountId = campaign.EmailAccountId.Value;
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

            await ProcessRecipientAsync(campaign, recipient, accountId, attachments, linked.Token);
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
        Guid accountId,
        IReadOnlyList<EmailAttachmentContent> attachments,
        CancellationToken cancellationToken)
    {
        if (campaign.TemplateId is not null)
        {
            var claimed = await _templateRecipientProtection.TryClaimAsync(
                campaign.TemplateId.Value,
                campaign.Id,
                recipient.Email,
                campaign.AllowAlreadyContacted,
                cancellationToken);

            if (!claimed)
            {
                recipient.Status = RecipientDeliveryStatus.Cancelled;
                recipient.FailureReason = "Already contacted for this template.";
                campaign.CurrentRecipientEmail = recipient.Email;
                RecalculateCounts(campaign);
                await _db.SaveChangesAsync(cancellationToken);
                await NotifyAsync(campaign, recipient, cancellationToken);
                return;
            }
        }

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
                await ReleaseTemplateClaimAsync(campaign, recipient);
                RecalculateCounts(campaign);
                await _db.SaveChangesAsync(CancellationToken.None);
                await NotifyAsync(campaign, recipient, CancellationToken.None);
                return;
            }

            recipient.AttemptCount++;
            recipient.LastAttemptAtUtc = DateTimeOffset.UtcNow;

            var mergeValues = TemplateMergeRenderer.BuildValues(
                recipient.Email,
                recipient.DisplayName,
                recipient.CompanyName,
                null,
                null,
                null,
                null,
                PersonalizationSerializer.Deserialize(recipient.PersonalizationJson));
            var subject = TemplateMergeRenderer.Render(campaign.Subject, mergeValues);
            var htmlBody = TemplateMergeRenderer.Render(campaign.HtmlBody, mergeValues);
            var plainTextBody = TemplateMergeRenderer.Render(campaign.PlainTextBody, mergeValues);

            lastResult = await _emailDelivery.SendAsync(new EmailDeliveryRequest
            {
                AccountId = accountId,
                ToEmail = recipient.Email,
                Subject = subject,
                HtmlBody = htmlBody,
                PlainTextBody = plainTextBody,
                Attachments = attachments
            }, cancellationToken);

            if (lastResult.Success)
            {
                recipient.Status = RecipientDeliveryStatus.Sent;
                recipient.FailureReason = null;
                recipient.IsTransientFailure = false;
                await MarkTemplateSentAsync(campaign, recipient, cancellationToken);
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
        await MarkTemplateFailedAsync(campaign, recipient, recipient.FailureReason, cancellationToken);
        RecalculateCounts(campaign);
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(campaign, recipient, cancellationToken);
        _logger.LogWarning("Failed to send campaign {CampaignId} to recipient {RecipientId}", campaign.Id, recipient.Id);
    }

    private async Task MarkTemplateSentAsync(
        Campaign campaign,
        CampaignRecipient recipient,
        CancellationToken cancellationToken)
    {
        if (campaign.TemplateId is null)
        {
            return;
        }

        await _templateRecipientProtection.MarkSentAsync(
            new MarkTemplateRecipientSentRequest
            {
                TemplateId = campaign.TemplateId.Value,
                CampaignId = campaign.Id,
                RecipientEmail = recipient.Email,
                Provider = campaign.Provider,
                SendingAccountId = campaign.EmailAccountId,
                SendingAccountEmail = campaign.SendingAccountEmail
            },
            cancellationToken);
    }

    private async Task MarkTemplateFailedAsync(
        Campaign campaign,
        CampaignRecipient recipient,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        if (campaign.TemplateId is null)
        {
            return;
        }

        await _templateRecipientProtection.MarkFailedAsync(
            campaign.TemplateId.Value,
            campaign.Id,
            recipient.Email,
            failureReason,
            cancellationToken);
    }

    private async Task ReleaseTemplateClaimAsync(Campaign campaign, CampaignRecipient recipient)
    {
        if (campaign.TemplateId is null)
        {
            return;
        }

        await _templateRecipientProtection.ReleaseClaimAsync(
            campaign.TemplateId.Value,
            campaign.Id,
            recipient.Email,
            CancellationToken.None);
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
            return "Email send failed.";
        }

        var sanitized = message.Replace("password", "****", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length > 500 ? sanitized[..500] : sanitized;
    }
}
