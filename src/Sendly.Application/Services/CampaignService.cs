using System.Text;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Sendly.Application.Services;

public class CampaignService
{
    private readonly ISendlyDbContext _db;
    private readonly ICampaignQueue _queue;
    private readonly IAttachmentStore _attachmentStore;
    private readonly IEmailSender _emailSender;
    private readonly SmtpSettingsService _smtpSettingsService;
    private readonly SignatureService _signatureService;
    private readonly CampaignCancellationRegistry _cancellationRegistry;
    private readonly EmailSendingOptions _sendingOptions;

    public CampaignService(
        ISendlyDbContext db,
        ICampaignQueue queue,
        IAttachmentStore attachmentStore,
        IEmailSender emailSender,
        SmtpSettingsService smtpSettingsService,
        SignatureService signatureService,
        CampaignCancellationRegistry cancellationRegistry,
        IOptions<EmailSendingOptions> sendingOptions)
    {
        _db = db;
        _queue = queue;
        _attachmentStore = attachmentStore;
        _emailSender = emailSender;
        _smtpSettingsService = smtpSettingsService;
        _signatureService = signatureService;
        _cancellationRegistry = cancellationRegistry;
        _sendingOptions = sendingOptions.Value;
    }

    public ValidateRecipientsResponseDto ValidateRecipients(ValidateRecipientsRequestDto request)
    {
        var parsed = EmailAddressParser.Parse(request.RawRecipients);
        return new ValidateRecipientsResponseDto
        {
            ValidEmails = parsed.ValidEmails.ToList(),
            InvalidEmails = parsed.InvalidEmails.ToList(),
            ValidCount = parsed.ValidCount,
            InvalidCount = parsed.InvalidCount
        };
    }

    public async Task<CampaignDetailDto> CreateAndStartAsync(CreateCampaignRequestDto request, CancellationToken cancellationToken)
    {
        await EnsureCanSendAsync(cancellationToken);
        EnsureNoActiveCampaign(await GetActiveCampaignAsync(cancellationToken));

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return await MapDetailAsync(existing.Id, cancellationToken);
            }
        }

        var parsed = EmailAddressParser.Parse(request.RawRecipients);
        EnsureValidCampaignInput(request, parsed);

        var signatureHtml = await _signatureService.ResolveSignatureHtmlAsync(request.SignatureId, cancellationToken);
        var htmlBody = SignatureComposer.Combine(request.HtmlBody, signatureHtml);
        var campaign = CreateCampaignEntity(request, parsed, htmlBody);
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.AttachmentIds.Count > 0)
        {
            await _attachmentStore.AssignToCampaignAsync(request.AttachmentIds, campaign.Id, cancellationToken);
        }

        await _queue.EnqueueAsync(campaign.Id, cancellationToken);
        return await MapDetailAsync(campaign.Id, cancellationToken);
    }

    public async Task<SmtpTestResultDto> SendTestEmailAsync(TestEmailRequestDto request, CancellationToken cancellationToken)
    {
        await EnsureCanSendAsync(cancellationToken);

        if (!EmailAddressParser.IsValidEmail(request.ToEmail))
        {
            throw new InvalidOperationException("A valid test recipient email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new InvalidOperationException("Subject is required.");
        }

        if (string.IsNullOrWhiteSpace(request.HtmlBody))
        {
            throw new InvalidOperationException("Email body is required.");
        }

        var credentials = await _smtpSettingsService.GetResolvedCredentialsAsync(cancellationToken);
        if (credentials is null)
        {
            throw new InvalidOperationException("SMTP configuration is incomplete.");
        }

        var attachments = await _attachmentStore.LoadAsync(request.AttachmentIds, cancellationToken);
        var signatureHtml = await _signatureService.ResolveSignatureHtmlAsync(request.SignatureId, cancellationToken);
        var htmlBody = SignatureComposer.Combine(request.HtmlBody, signatureHtml);
        var result = await _emailSender.SendAsync(new EmailSendRequest
        {
            ToEmail = request.ToEmail.Trim(),
            Subject = request.Subject.Trim(),
            HtmlBody = htmlBody,
            PlainTextBody = HtmlToPlainTextConverter.Convert(htmlBody),
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

        return new SmtpTestResultDto
        {
            Success = result.Success,
            Message = result.Success
                ? "Test email was accepted by the SMTP server."
                : SanitizeError(result.Message)
        };
    }

    public async Task<List<CampaignSummaryDto>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var campaigns = await _db.Campaigns
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        SortCampaignsByCreatedDescending(campaigns);

        var summaries = new List<CampaignSummaryDto>();
        foreach (var campaign in campaigns)
        {
            if (!MatchesSearch(campaign, search))
            {
                continue;
            }

            summaries.Add(MapSummary(campaign));
        }

        return summaries;
    }

    public async Task<CampaignDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await MapDetailAsync(id, cancellationToken);
    }

    public async Task<List<CampaignRecipientDto>> GetRecipientsAsync(
        Guid id,
        string? statusFilter,
        CancellationToken cancellationToken)
    {
        var recipients = await _db.CampaignRecipients
            .AsNoTracking()
            .Where(x => x.CampaignId == id)
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);

        var result = new List<CampaignRecipientDto>();
        foreach (var recipient in recipients)
        {
            if (!MatchesStatusFilter(recipient.Status, statusFilter))
            {
                continue;
            }

            result.Add(MapRecipient(recipient));
        }

        return result;
    }

    public async Task CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (campaign is null)
        {
            throw new InvalidOperationException("Campaign was not found.");
        }

        if (campaign.Status is CampaignStatus.Completed or CampaignStatus.CompletedWithFailures or CampaignStatus.Cancelled)
        {
            return;
        }

        _cancellationRegistry.Cancel(id);
        campaign.Status = CampaignStatus.Cancelled;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RetryFailedAsync(Guid id, CancellationToken cancellationToken)
    {
        await EnsureCanSendAsync(cancellationToken);
        EnsureNoActiveCampaign(await GetActiveCampaignAsync(cancellationToken));

        var campaign = await _db.Campaigns
            .Include(x => x.Recipients)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (campaign is null)
        {
            throw new InvalidOperationException("Campaign was not found.");
        }

        var retryable = GetRetryableRecipients(campaign.Recipients);
        if (retryable.Count == 0)
        {
            throw new InvalidOperationException("No failed recipients are eligible for retry.");
        }

        ResetRecipientsForRetry(retryable);
        RecalculateCounts(campaign);
        campaign.Status = CampaignStatus.Queued;
        campaign.CompletedAtUtc = null;
        campaign.StartedAtUtc = null;
        campaign.CurrentRecipientEmail = null;
        await _db.SaveChangesAsync(cancellationToken);
        await _queue.EnqueueAsync(campaign.Id, cancellationToken);
    }

    public async Task<byte[]> ExportCsvAsync(Guid id, CancellationToken cancellationToken)
    {
        var recipients = await GetRecipientsAsync(id, null, cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("Email,Status,AttemptCount,LastAttemptAtUtc,FailureReason");

        foreach (var recipient in recipients)
        {
            var line = string.Join(',',
                EscapeCsv(recipient.Email),
                EscapeCsv(recipient.Status.ToString()),
                recipient.AttemptCount,
                EscapeCsv(recipient.LastAttemptAtUtc?.ToString("O") ?? string.Empty),
                EscapeCsv(recipient.FailureReason ?? string.Empty));
            builder.AppendLine(line);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private async Task EnsureCanSendAsync(CancellationToken cancellationToken)
    {
        var smtp = await _smtpSettingsService.GetAsync(cancellationToken);
        if (!smtp.IsConfigured || !smtp.HasPassword)
        {
            throw new InvalidOperationException("SMTP configuration is incomplete.");
        }
    }

    private static void EnsureNoActiveCampaign(Campaign? active)
    {
        if (active is not null)
        {
            throw new InvalidOperationException("Another campaign is already running. Wait for it to finish or cancel it first.");
        }
    }

    private async Task<Campaign?> GetActiveCampaignAsync(CancellationToken cancellationToken)
    {
        var active = await _db.Campaigns
            .AsNoTracking()
            .Where(x => x.Status == CampaignStatus.Queued || x.Status == CampaignStatus.Running)
            .ToListAsync(cancellationToken);

        SortCampaignsByCreatedDescending(active);
        return active.Count > 0 ? active[0] : null;
    }

    private async Task<Campaign?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken)
    {
        return await _db.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
    }

    private static void EnsureValidCampaignInput(CreateCampaignRequestDto request, EmailParseResult parsed)
    {
        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new InvalidOperationException("Subject is required.");
        }

        if (string.IsNullOrWhiteSpace(request.HtmlBody))
        {
            throw new InvalidOperationException("Email body is required.");
        }

        if (parsed.ValidCount == 0)
        {
            throw new InvalidOperationException("At least one valid recipient is required.");
        }

        if (parsed.InvalidCount > 0)
        {
            throw new InvalidOperationException("Remove invalid recipients before sending.");
        }
    }

    private Campaign CreateCampaignEntity(
        CreateCampaignRequestDto request,
        EmailParseResult parsed,
        string htmlBody)
    {
        var campaign = new Campaign
        {
            Subject = request.Subject.Trim(),
            HtmlBody = htmlBody,
            PlainTextBody = HtmlToPlainTextConverter.Convert(htmlBody),
            Status = CampaignStatus.Queued,
            TotalRecipients = parsed.ValidCount,
            PendingCount = parsed.ValidCount,
            DelayMilliseconds = request.DelayMilliseconds ?? _sendingOptions.DefaultDelayMilliseconds,
            BatchSize = request.BatchSize ?? _sendingOptions.DefaultBatchSize,
            MaxRetryCount = request.MaxRetryCount ?? _sendingOptions.DefaultMaxRetryCount,
            IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        foreach (var email in parsed.ValidEmails)
        {
            campaign.Recipients.Add(new CampaignRecipient
            {
                Email = email,
                Status = RecipientDeliveryStatus.Pending
            });
        }

        return campaign;
    }

    private async Task<CampaignDetailDto> MapDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns
            .AsNoTracking()
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (campaign is null)
        {
            throw new InvalidOperationException("Campaign was not found.");
        }

        var detail = new CampaignDetailDto
        {
            Id = campaign.Id,
            Subject = campaign.Subject,
            Status = campaign.Status,
            TotalRecipients = campaign.TotalRecipients,
            SuccessCount = campaign.SuccessCount,
            FailedCount = campaign.FailedCount,
            CancelledCount = campaign.CancelledCount,
            PendingCount = campaign.PendingCount,
            ProgressPercent = CalculateProgress(campaign),
            CurrentRecipientEmail = campaign.CurrentRecipientEmail,
            CreatedAtUtc = campaign.CreatedAtUtc,
            StartedAtUtc = campaign.StartedAtUtc,
            CompletedAtUtc = campaign.CompletedAtUtc,
            HtmlBody = campaign.HtmlBody
        };

        foreach (var attachment in campaign.Attachments)
        {
            detail.Attachments.Add(new CampaignAttachmentDto
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes
            });
        }

        return detail;
    }

    private static CampaignSummaryDto MapSummary(Campaign campaign)
    {
        return new CampaignSummaryDto
        {
            Id = campaign.Id,
            Subject = campaign.Subject,
            Status = campaign.Status,
            TotalRecipients = campaign.TotalRecipients,
            SuccessCount = campaign.SuccessCount,
            FailedCount = campaign.FailedCount,
            CancelledCount = campaign.CancelledCount,
            PendingCount = campaign.PendingCount,
            ProgressPercent = CalculateProgress(campaign),
            CurrentRecipientEmail = campaign.CurrentRecipientEmail,
            CreatedAtUtc = campaign.CreatedAtUtc,
            StartedAtUtc = campaign.StartedAtUtc,
            CompletedAtUtc = campaign.CompletedAtUtc
        };
    }

    private static CampaignRecipientDto MapRecipient(CampaignRecipient recipient)
    {
        return new CampaignRecipientDto
        {
            Id = recipient.Id,
            Email = recipient.Email,
            Status = recipient.Status,
            AttemptCount = recipient.AttemptCount,
            LastAttemptAtUtc = recipient.LastAttemptAtUtc,
            FailureReason = recipient.FailureReason
        };
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

    private static bool MatchesSearch(Campaign campaign, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return campaign.Subject.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesStatusFilter(RecipientDeliveryStatus status, string? statusFilter)
    {
        if (string.IsNullOrWhiteSpace(statusFilter) || statusFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return status.ToString().Equals(statusFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static List<CampaignRecipient> GetRetryableRecipients(ICollection<CampaignRecipient> recipients)
    {
        var result = new List<CampaignRecipient>();
        foreach (var recipient in recipients)
        {
            if (recipient.Status == RecipientDeliveryStatus.Failed && recipient.IsTransientFailure)
            {
                result.Add(recipient);
            }
        }

        return result;
    }

    private static void ResetRecipientsForRetry(List<CampaignRecipient> recipients)
    {
        foreach (var recipient in recipients)
        {
            recipient.Status = RecipientDeliveryStatus.Pending;
            recipient.FailureReason = null;
            recipient.IsTransientFailure = false;
        }
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
        campaign.TotalRecipients = campaign.Recipients.Count;
    }

    private static void SortCampaignsByCreatedDescending(List<Campaign> campaigns)
    {
        campaigns.Sort(static (left, right) => right.CreatedAtUtc.CompareTo(left.CreatedAtUtc));
    }

    private static string EscapeCsv(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static string SanitizeError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "SMTP operation failed.";
        }

        var sanitized = message;
        sanitized = sanitized.Replace("password", "****", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length > 500 ? sanitized[..500] : sanitized;
    }
}
