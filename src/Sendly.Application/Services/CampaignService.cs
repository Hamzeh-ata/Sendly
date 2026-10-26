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
    private readonly IEmailDeliveryService _emailDelivery;
    private readonly EmailAccountService _emailAccountService;
    private readonly SignatureService _signatureService;
    private readonly TemplateRecipientProtectionService _templateRecipientProtection;
    private readonly CampaignCancellationRegistry _cancellationRegistry;
    private readonly EmailSendingOptions _sendingOptions;

    public CampaignService(
        ISendlyDbContext db,
        ICampaignQueue queue,
        IAttachmentStore attachmentStore,
        IEmailDeliveryService emailDelivery,
        EmailAccountService emailAccountService,
        SignatureService signatureService,
        TemplateRecipientProtectionService templateRecipientProtection,
        CampaignCancellationRegistry cancellationRegistry,
        IOptions<EmailSendingOptions> sendingOptions)
    {
        _db = db;
        _queue = queue;
        _attachmentStore = attachmentStore;
        _emailDelivery = emailDelivery;
        _emailAccountService = emailAccountService;
        _signatureService = signatureService;
        _templateRecipientProtection = templateRecipientProtection;
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
        await _emailAccountService.EnsureAccountReadyAsync(request.EmailAccountId, cancellationToken);
        await EnsureCanStartCampaignAsync(cancellationToken);

        var existing = await FindExistingByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await MapDetailAsync(existing.Id, cancellationToken);
        }

        var campaign = await CreateCampaignInternalAsync(request, CampaignStatus.Queued, cancellationToken);
        await _queue.EnqueueAsync(campaign.Id, cancellationToken);
        return await MapDetailAsync(campaign.Id, cancellationToken);
    }

    public async Task<CampaignDetailDto> CreateDraftAsync(CreateCampaignRequestDto request, CancellationToken cancellationToken)
    {
        await _emailAccountService.EnsureAccountReadyAsync(request.EmailAccountId, cancellationToken);

        var existing = await FindExistingByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await MapDetailAsync(existing.Id, cancellationToken);
        }

        var campaign = await CreateCampaignInternalAsync(request, CampaignStatus.Draft, cancellationToken);
        return await MapDetailAsync(campaign.Id, cancellationToken);
    }

    public async Task StartAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (campaign is null)
        {
            throw new InvalidOperationException("Campaign was not found.");
        }

        if (campaign.Status != CampaignStatus.Draft)
        {
            throw new InvalidOperationException("Only draft campaigns can be started.");
        }

        await EnsureDraftCanStartWithTemplateProtectionAsync(campaign, cancellationToken);
        await _emailAccountService.EnsureAccountReadyAsync(campaign.EmailAccountId, cancellationToken);
        await EnsureCanStartCampaignAsync(cancellationToken);

        campaign.Status = CampaignStatus.Queued;
        campaign.CompletedAtUtc = null;
        campaign.StartedAtUtc = null;
        campaign.CurrentRecipientEmail = null;
        await _db.SaveChangesAsync(cancellationToken);
        await _queue.EnqueueAsync(campaign.Id, cancellationToken);
    }

    public async Task<EmailTestResultDto> SendTestEmailAsync(TestEmailRequestDto request, CancellationToken cancellationToken)
    {
        await _emailAccountService.EnsureAccountReadyAsync(request.EmailAccountId, cancellationToken);

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

        var account = await ResolveAccountSnapshotAsync(request.EmailAccountId, cancellationToken);
        var attachments = await _attachmentStore.LoadAsync(request.AttachmentIds, cancellationToken);
        var signatureHtml = await _signatureService.ResolveSignatureHtmlAsync(request.SignatureId, cancellationToken);
        var htmlBody = SignatureComposer.Combine(request.HtmlBody, signatureHtml);
        var result = await _emailDelivery.SendAsync(new EmailDeliveryRequest
        {
            AccountId = account.AccountId,
            ToEmail = request.ToEmail.Trim(),
            Subject = request.Subject.Trim(),
            HtmlBody = htmlBody,
            PlainTextBody = HtmlToPlainTextConverter.Convert(htmlBody),
            Attachments = attachments
        }, cancellationToken);

        return new EmailTestResultDto
        {
            Success = result.Success,
            Message = result.Success
                ? "Test email was accepted by the email provider."
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

    public async Task<CheckTemplateRecipientsResponseDto> CheckTemplateRecipientsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (campaign is null)
        {
            throw new InvalidOperationException("Campaign was not found.");
        }

        if (campaign.TemplateId is null)
        {
            return new CheckTemplateRecipientsResponseDto();
        }

        var recipients = await _db.CampaignRecipients.AsNoTracking()
            .Where(x => x.CampaignId == id && x.Status == RecipientDeliveryStatus.Pending)
            .Select(x => x.Email)
            .ToListAsync(cancellationToken);

        var rawRecipients = string.Join('\n', recipients);
        return await _templateRecipientProtection.CheckRecipientsAsync(
            campaign.TemplateId.Value,
            new CheckTemplateRecipientsRequestDto { RawRecipients = rawRecipients },
            cancellationToken);
    }

    public async Task<CampaignDetailDto> SetAllowAlreadyContactedAsync(
        Guid id,
        bool allowAlreadyContacted,
        CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (campaign is null)
        {
            throw new InvalidOperationException("Campaign was not found.");
        }

        if (campaign.Status != CampaignStatus.Draft)
        {
            throw new InvalidOperationException("Only draft campaigns can change already-contacted settings.");
        }

        campaign.AllowAlreadyContacted = allowAlreadyContacted;
        await _db.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(id, cancellationToken);
    }

    public async Task<CampaignDetailDto> ExcludeAlreadyContactedAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (campaign is null)
        {
            throw new InvalidOperationException("Campaign was not found.");
        }

        if (campaign.Status != CampaignStatus.Draft)
        {
            throw new InvalidOperationException("Only draft campaigns can exclude already-contacted recipients.");
        }

        if (campaign.TemplateId is null)
        {
            throw new InvalidOperationException("This campaign has no template to check against.");
        }

        var pending = await _db.CampaignRecipients
            .Where(x => x.CampaignId == id && x.Status == RecipientDeliveryStatus.Pending)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return await MapDetailAsync(id, cancellationToken);
        }

        var emails = new List<string>();
        foreach (var recipient in pending)
        {
            emails.Add(recipient.Email);
        }

        var check = await _templateRecipientProtection.CheckRecipientsAsync(
            campaign.TemplateId.Value,
            new CheckTemplateRecipientsRequestDto { RawRecipients = string.Join('\n', emails) },
            cancellationToken);

        var contacted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in check.Recipients)
        {
            if (item.AlreadyContacted)
            {
                contacted.Add(item.Email);
            }
        }

        var remaining = 0;
        var removed = 0;
        foreach (var recipient in pending)
        {
            if (contacted.Contains(recipient.Email))
            {
                recipient.Status = RecipientDeliveryStatus.Cancelled;
                recipient.FailureReason = "Already contacted for this template.";
                removed++;
                continue;
            }

            remaining++;
        }

        if (removed == 0)
        {
            return await MapDetailAsync(id, cancellationToken);
        }

        if (remaining <= 0)
        {
            throw new InvalidOperationException(
                "All pending recipients were already contacted for this template. Nothing left to send.");
        }

        campaign.AllowAlreadyContacted = false;
        RecalculateCounts(campaign);
        await _db.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(id, cancellationToken);
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
        var campaign = await _db.Campaigns
            .Include(x => x.Recipients)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (campaign is null)
        {
            throw new InvalidOperationException("Campaign was not found.");
        }

        await _emailAccountService.EnsureAccountReadyAsync(campaign.EmailAccountId, cancellationToken);
        await EnsureCanStartCampaignAsync(cancellationToken);

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

    private async Task<ResolvedSendingAccount> ResolveAccountSnapshotAsync(
        Guid? emailAccountId,
        CancellationToken cancellationToken)
    {
        var account = await _emailAccountService.ResolveSendingAccountAsync(emailAccountId, cancellationToken);
        if (account is null)
        {
            throw new InvalidOperationException("A connected email account is required before sending.");
        }

        return account;
    }

    private async Task EnsureCanStartCampaignAsync(CancellationToken cancellationToken)
    {
        await RecoverStaleActiveCampaignsAsync(cancellationToken);
        var active = await GetActiveCampaignAsync(cancellationToken);
        if (active is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Another campaign is already running ({active.Id}). Open Campaigns to wait or cancel it first.");
    }

    private async Task RecoverStaleActiveCampaignsAsync(CancellationToken cancellationToken)
    {
        var actives = await _db.Campaigns
            .Include(x => x.Recipients)
            .Where(x => x.Status == CampaignStatus.Queued || x.Status == CampaignStatus.Running)
            .ToListAsync(cancellationToken);

        if (actives.Count == 0)
        {
            return;
        }

        var changed = false;
        foreach (var campaign in actives)
        {
            if (CloseIfNoPendingWork(campaign))
            {
                changed = true;
                continue;
            }

            if (IsOrphanedActiveCampaign(campaign))
            {
                AbandonInterruptedCampaign(campaign);
                changed = true;
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        foreach (var campaign in actives)
        {
            if (campaign.Status is CampaignStatus.Queued or CampaignStatus.Running)
            {
                await _queue.EnqueueAsync(campaign.Id, cancellationToken);
            }
        }
    }

    private static bool CloseIfNoPendingWork(Campaign campaign)
    {
        var pending = 0;
        foreach (var recipient in campaign.Recipients)
        {
            if (recipient.Status is RecipientDeliveryStatus.Pending or RecipientDeliveryStatus.Sending)
            {
                pending++;
            }
        }

        if (pending > 0)
        {
            return false;
        }

        FinalizeClosedCampaign(campaign);
        return true;
    }

    private static bool IsOrphanedActiveCampaign(Campaign campaign)
    {
        if (campaign.Status == CampaignStatus.Queued && campaign.StartedAtUtc is null)
        {
            return true;
        }

        var reference = campaign.StartedAtUtc ?? campaign.CreatedAtUtc;
        var age = DateTimeOffset.UtcNow - reference;
        return campaign.Status == CampaignStatus.Running && age > TimeSpan.FromMinutes(30);
    }

    private static void AbandonInterruptedCampaign(Campaign campaign)
    {
        foreach (var recipient in campaign.Recipients)
        {
            if (recipient.Status is RecipientDeliveryStatus.Pending or RecipientDeliveryStatus.Sending)
            {
                recipient.Status = RecipientDeliveryStatus.Cancelled;
                recipient.FailureReason = "Campaign interrupted and recovered.";
            }
        }

        campaign.Status = CampaignStatus.Cancelled;
        campaign.CurrentRecipientEmail = null;
        campaign.CompletedAtUtc = DateTimeOffset.UtcNow;
        RecountCampaign(campaign);
        FinalizeClosedCampaign(campaign);
    }

    private static void FinalizeClosedCampaign(Campaign campaign)
    {
        RecountCampaign(campaign);
        if (campaign.Status == CampaignStatus.Cancelled)
        {
            campaign.CompletedAtUtc ??= DateTimeOffset.UtcNow;
            campaign.CurrentRecipientEmail = null;
            return;
        }

        if (campaign.FailedCount > 0 && campaign.SuccessCount > 0)
        {
            campaign.Status = CampaignStatus.CompletedWithFailures;
        }
        else if (campaign.FailedCount > 0 && campaign.SuccessCount == 0)
        {
            campaign.Status = CampaignStatus.Failed;
        }
        else
        {
            campaign.Status = CampaignStatus.Completed;
        }

        campaign.CompletedAtUtc ??= DateTimeOffset.UtcNow;
        campaign.CurrentRecipientEmail = null;
    }

    private static void RecountCampaign(Campaign campaign)
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

    private async Task<(Guid? TemplateId, string? TemplateName)> ResolveTemplateSnapshotAsync(
        Guid? templateId,
        CancellationToken cancellationToken)
    {
        if (templateId is null)
        {
            return (null, null);
        }

        var template = await _db.EmailTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == templateId.Value, cancellationToken);

        if (template is null)
        {
            throw new InvalidOperationException("Selected template was not found.");
        }

        return (template.Id, template.Name);
    }

    private async Task<List<string>> ResolveSendableEmailsAsync(
        Guid? templateId,
        IReadOnlyList<string> emails,
        bool allowAlreadyContacted,
        CancellationToken cancellationToken)
    {
        if (templateId is null)
        {
            return emails.ToList();
        }

        return await _templateRecipientProtection.FilterSendableEmailsAsync(
            templateId.Value,
            emails,
            allowAlreadyContacted,
            cancellationToken);
    }

    private static void EnsureHasSendableRecipients(
        List<string> sendableEmails,
        Guid? templateId,
        bool allowAlreadyContacted)
    {
        if (sendableEmails.Count > 0)
        {
            return;
        }

        if (templateId is not null && !allowAlreadyContacted)
        {
            throw new InvalidOperationException(
                "All recipients have already received this template. Remove duplicates or allow already contacted recipients.");
        }

        throw new InvalidOperationException("At least one valid recipient is required.");
    }

    private async Task<Campaign?> FindExistingByIdempotencyKeyAsync(
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        return await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
    }

    private async Task<Campaign> CreateCampaignInternalAsync(
        CreateCampaignRequestDto request,
        CampaignStatus status,
        CancellationToken cancellationToken)
    {
        var parsed = ResolveParsedRecipients(request);
        EnsureValidCampaignInput(request, parsed);

        var signatureHtml = await _signatureService.ResolveSignatureHtmlAsync(request.SignatureId, cancellationToken);
        var htmlBody = SignatureComposer.Combine(request.HtmlBody, signatureHtml);
        var templateSnapshot = await ResolveTemplateSnapshotAsync(request.TemplateId, cancellationToken);
        var accountSnapshot = await ResolveAccountSnapshotAsync(request.EmailAccountId, cancellationToken);
        var sendableEmails = await ResolveSendableEmailsAsync(
            templateSnapshot.TemplateId,
            parsed.ValidEmails,
            request.AllowAlreadyContacted,
            cancellationToken);
        EnsureHasSendableRecipients(sendableEmails, templateSnapshot.TemplateId, request.AllowAlreadyContacted);

        var personalizationMap = await BuildPersonalizationMapAsync(
            request,
            sendableEmails,
            templateSnapshot.TemplateId,
            cancellationToken);
        var campaign = CreateCampaignEntity(
            request,
            sendableEmails,
            htmlBody,
            templateSnapshot,
            accountSnapshot,
            personalizationMap,
            status);
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.AttachmentIds.Count > 0)
        {
            await _attachmentStore.AssignToCampaignAsync(request.AttachmentIds, campaign.Id, cancellationToken);
        }

        return campaign;
    }

    private Campaign CreateCampaignEntity(
        CreateCampaignRequestDto request,
        List<string> sendableEmails,
        string htmlBody,
        (Guid? TemplateId, string? TemplateName) templateSnapshot,
        ResolvedSendingAccount accountSnapshot,
        Dictionary<string, CreateCampaignRecipientDto> personalizationMap,
        CampaignStatus status)
    {
        var campaign = new Campaign
        {
            Subject = request.Subject.Trim(),
            HtmlBody = htmlBody,
            PlainTextBody = HtmlToPlainTextConverter.Convert(htmlBody),
            Status = status,
            TotalRecipients = sendableEmails.Count,
            PendingCount = sendableEmails.Count,
            DelayMilliseconds = request.DelayMilliseconds ?? _sendingOptions.DefaultDelayMilliseconds,
            BatchSize = request.BatchSize ?? _sendingOptions.DefaultBatchSize,
            MaxRetryCount = request.MaxRetryCount ?? _sendingOptions.DefaultMaxRetryCount,
            IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim(),
            TemplateId = templateSnapshot.TemplateId,
            TemplateName = templateSnapshot.TemplateName,
            EmailAccountId = accountSnapshot.AccountId,
            Provider = accountSnapshot.Provider,
            SendingAccountName = accountSnapshot.DisplayName,
            SendingAccountEmail = accountSnapshot.EmailAddress,
            AllowAlreadyContacted = request.AllowAlreadyContacted,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        foreach (var email in sendableEmails)
        {
            var recipient = new CampaignRecipient
            {
                Email = email,
                Status = RecipientDeliveryStatus.Pending
            };

            var key = EmailAddressParser.Normalize(email);
            if (personalizationMap.TryGetValue(key, out var personalized))
            {
                recipient.ProspectId = personalized.ProspectId;
                recipient.DisplayName = TrimOrNull(personalized.DisplayName);
                recipient.CompanyName = TrimOrNull(personalized.CompanyName);
                recipient.PersonalizationJson = PersonalizationSerializer.Serialize(personalized.Personalization);
            }

            campaign.Recipients.Add(recipient);
        }

        return campaign;
    }

    private static EmailParseResult ResolveParsedRecipients(CreateCampaignRequestDto request)
    {
        if (request.PersonalizedRecipients.Count > 0)
        {
            var emails = new List<string>();
            foreach (var item in request.PersonalizedRecipients)
            {
                if (!string.IsNullOrWhiteSpace(item.Email))
                {
                    emails.Add(item.Email);
                }
            }

            return EmailAddressParser.Parse(string.Join('\n', emails));
        }

        return EmailAddressParser.Parse(request.RawRecipients);
    }

    private static Dictionary<string, CreateCampaignRecipientDto> BuildPersonalizationMap(CreateCampaignRequestDto request)
    {
        var map = new Dictionary<string, CreateCampaignRecipientDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.PersonalizedRecipients)
        {
            if (string.IsNullOrWhiteSpace(item.Email))
            {
                continue;
            }

            var key = EmailAddressParser.Normalize(item.Email);
            map[key] = item;
        }

        return map;
    }

    private async Task<Dictionary<string, CreateCampaignRecipientDto>> BuildPersonalizationMapAsync(
        CreateCampaignRequestDto request,
        List<string> sendableEmails,
        Guid? templateId,
        CancellationToken cancellationToken)
    {
        var map = BuildPersonalizationMap(request);
        await EnrichPersonalizationFromProspectsAsync(map, sendableEmails, templateId, cancellationToken);
        return map;
    }

    private async Task EnrichPersonalizationFromProspectsAsync(
        Dictionary<string, CreateCampaignRecipientDto> map,
        List<string> sendableEmails,
        Guid? templateId,
        CancellationToken cancellationToken)
    {
        var missing = CollectMissingPersonalizationEmails(map, sendableEmails);
        if (missing.Count == 0)
        {
            return;
        }

        var candidates = await LoadProspectCandidatesAsync(missing, cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        var preferredProjectIds = await LoadTemplateProjectIdsAsync(templateId, cancellationToken);
        foreach (var email in missing)
        {
            var prospect = ChooseProspectForEmail(candidates, email, preferredProjectIds, templateId);
            if (prospect is null)
            {
                continue;
            }

            map[email] = BuildProspectRecipient(prospect);
        }
    }

    private static List<string> CollectMissingPersonalizationEmails(
        Dictionary<string, CreateCampaignRecipientDto> map,
        List<string> sendableEmails)
    {
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var email in sendableEmails)
        {
            var key = EmailAddressParser.Normalize(email);
            if (!seen.Add(key))
            {
                continue;
            }

            if (map.TryGetValue(key, out var existing) && !NeedsProspectPersonalization(existing))
            {
                continue;
            }

            missing.Add(key);
        }

        return missing;
    }

    private static bool NeedsProspectPersonalization(CreateCampaignRecipientDto recipient)
    {
        return string.IsNullOrWhiteSpace(recipient.CompanyName)
            && string.IsNullOrWhiteSpace(recipient.DisplayName)
            && recipient.Personalization.Count == 0;
    }

    private async Task<List<Prospect>> LoadProspectCandidatesAsync(
        List<string> normalizedEmails,
        CancellationToken cancellationToken)
    {
        return await _db.Prospects
            .AsNoTracking()
            .Where(x => x.NormalizedEmail != null && normalizedEmails.Contains(x.NormalizedEmail))
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<Guid>> LoadTemplateProjectIdsAsync(
        Guid? templateId,
        CancellationToken cancellationToken)
    {
        if (templateId is null)
        {
            return new HashSet<Guid>();
        }

        var projectIds = await _db.ProjectTemplates
            .AsNoTracking()
            .Where(x => x.TemplateId == templateId.Value)
            .Select(x => x.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return new HashSet<Guid>(projectIds);
    }

    private static Prospect? ChooseProspectForEmail(
        List<Prospect> candidates,
        string normalizedEmail,
        HashSet<Guid> preferredProjectIds,
        Guid? templateId)
    {
        Prospect? best = null;
        var bestScore = -1;
        foreach (var candidate in candidates)
        {
            if (!string.Equals(candidate.NormalizedEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var score = ScoreProspect(candidate, preferredProjectIds, templateId);
            if (best is null || score > bestScore || (score == bestScore && candidate.UpdatedAtUtc > best.UpdatedAtUtc))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static int ScoreProspect(Prospect prospect, HashSet<Guid> preferredProjectIds, Guid? templateId)
    {
        var score = 0;
        if (preferredProjectIds.Contains(prospect.ProjectId))
        {
            score += 4;
        }

        if (templateId.HasValue && prospect.RecommendedTemplateId == templateId.Value)
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(prospect.CompanyName))
        {
            score += 1;
        }

        return score;
    }

    private static CreateCampaignRecipientDto BuildProspectRecipient(Prospect prospect)
    {
        return new CreateCampaignRecipientDto
        {
            Email = prospect.Email ?? prospect.NormalizedEmail ?? string.Empty,
            ProspectId = prospect.Id,
            DisplayName = TrimOrNull(prospect.ContactName),
            CompanyName = TrimOrNull(prospect.CompanyName),
            Personalization = BuildProspectPersonalization(prospect)
        };
    }

    private static Dictionary<string, string> BuildProspectPersonalization(Prospect prospect)
    {
        var values = PersonalizationSerializer.Deserialize(prospect.PersonalizationJson);
        AddPersonalizationValue(values, "CompanyName", prospect.CompanyName);
        AddPersonalizationValue(values, "AgencyName", prospect.CompanyName);
        AddPersonalizationValue(values, "ContactName", prospect.ContactName);
        AddPersonalizationValue(values, "Industry", prospect.Industry);
        AddPersonalizationValue(values, "City", prospect.City);
        AddPersonalizationValue(values, "Country", prospect.Country);
        AddPersonalizationValue(values, "ContactRole", prospect.ContactRole);
        AddPersonalizationValue(values, "Email", prospect.Email);
        return values;
    }

    private static void AddPersonalizationValue(Dictionary<string, string> values, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (values.TryGetValue(key, out var existing) && !string.IsNullOrWhiteSpace(existing))
        {
            return;
        }

        values[key] = value.Trim();
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
            TemplateId = campaign.TemplateId,
            TemplateName = campaign.TemplateName,
            EmailAccountId = campaign.EmailAccountId,
            Provider = campaign.Provider,
            SendingAccountName = campaign.SendingAccountName,
            SendingAccountEmail = campaign.SendingAccountEmail,
            AllowAlreadyContacted = campaign.AllowAlreadyContacted,
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
            CompletedAtUtc = campaign.CompletedAtUtc,
            TemplateId = campaign.TemplateId,
            TemplateName = campaign.TemplateName,
            EmailAccountId = campaign.EmailAccountId,
            Provider = campaign.Provider,
            SendingAccountName = campaign.SendingAccountName,
            SendingAccountEmail = campaign.SendingAccountEmail,
            AllowAlreadyContacted = campaign.AllowAlreadyContacted
        };
    }

    private async Task EnsureDraftCanStartWithTemplateProtectionAsync(
        Campaign campaign,
        CancellationToken cancellationToken)
    {
        if (campaign.TemplateId is null || campaign.AllowAlreadyContacted)
        {
            return;
        }

        var check = await CheckTemplateRecipientsAsync(campaign.Id, cancellationToken);
        if (check.AlreadyContactedCount <= 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{check.AlreadyContactedCount} recipient(s) were already emailed with this template. Remove them or allow resend before starting.");
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

        var term = search.Trim();
        if (campaign.Subject.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (campaign.TemplateName is not null
            && campaign.TemplateName.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
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
            return "Email send failed.";
        }

        var sanitized = message;
        sanitized = sanitized.Replace("password", "****", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length > 500 ? sanitized[..500] : sanitized;
    }
}
