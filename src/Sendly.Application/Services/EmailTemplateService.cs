using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class EmailTemplateService
{
    private readonly ISendlyDbContext _db;
    private readonly IAttachmentStore _attachmentStore;
    private readonly TemplateRecipientProtectionService _recipientProtection;

    public EmailTemplateService(
        ISendlyDbContext db,
        IAttachmentStore attachmentStore,
        TemplateRecipientProtectionService recipientProtection)
    {
        _db = db;
        _attachmentStore = attachmentStore;
        _recipientProtection = recipientProtection;
    }

    public async Task<List<EmailTemplateSummaryDto>> ListAsync(
        string? search,
        string? status,
        CancellationToken cancellationToken)
    {
        var templates = await _db.EmailTemplates
            .AsNoTracking()
            .Include(x => x.Signature)
            .Include(x => x.EmailAccount)
            .Include(x => x.Attachments)
            .ToListAsync(cancellationToken);

        SortByUpdatedDescending(templates);

        var templateIds = new List<Guid>();
        foreach (var template in templates)
        {
            templateIds.Add(template.Id);
        }

        var statsMap = await _recipientProtection.GetStatsMapAsync(templateIds, cancellationToken);

        var result = new List<EmailTemplateSummaryDto>();
        foreach (var template in templates)
        {
            if (!MatchesSearch(template, search))
            {
                continue;
            }

            if (!MatchesStatus(template, status))
            {
                continue;
            }

            statsMap.TryGetValue(template.Id, out var stats);
            result.Add(MapSummary(template, stats));
        }

        return result;
    }

    public async Task<EmailTemplateDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await LoadTemplateAsync(id, cancellationToken);
        if (template is null)
        {
            return null;
        }

        var stats = await _recipientProtection.GetStatsAsync(id, cancellationToken);
        return MapDetail(template, stats);
    }

    public async Task<EmailTemplatePreviewDto?> PreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await LoadTemplateAsync(id, cancellationToken);
        if (template is null)
        {
            return null;
        }

        var signatureHtml = template.Signature?.HtmlContent;
        return new EmailTemplatePreviewDto
        {
            Id = template.Id,
            Name = template.Name,
            Subject = template.Subject,
            HtmlBody = SignatureComposer.Combine(template.HtmlBody, signatureHtml),
            SignatureName = template.Signature?.Name,
            SignatureHtml = signatureHtml,
            Attachments = MapAttachments(template.Attachments)
        };
    }

    public async Task<EmailTemplateDetailDto> CreateAsync(
        UpsertEmailTemplateDto request,
        CancellationToken cancellationToken)
    {
        ValidateUpsert(request);
        await EnsureSignatureExistsAsync(request.SignatureId, cancellationToken);
        await EnsureEmailAccountExistsAsync(request.EmailAccountId, cancellationToken);

        var template = new EmailTemplate
        {
            Name = request.Name.Trim(),
            Description = NormalizeOptional(request.Description),
            Subject = request.Subject.Trim(),
            HtmlBody = request.HtmlBody.Trim(),
            SignatureId = request.SignatureId,
            EmailAccountId = request.EmailAccountId,
            Category = NormalizeOptional(request.Category),
            WhatsAppTemplateName = NormalizeOptional(request.WhatsAppTemplateName),
            WhatsAppTemplateLanguage = NormalizeOptional(request.WhatsAppTemplateLanguage),
            WhatsAppHeaderParameter = NormalizeOptional(request.WhatsAppHeaderParameter),
            WhatsAppButtonSubType = NormalizeOptional(request.WhatsAppButtonSubType),
            WhatsAppButtonIndex = request.WhatsAppButtonIndex,
            WhatsAppButtonParameter = NormalizeOptional(request.WhatsAppButtonParameter),
            IsActive = request.IsActive,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.EmailTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.AttachmentIds.Count > 0)
        {
            await _attachmentStore.AssignToTemplateAsync(request.AttachmentIds, template.Id, cancellationToken);
        }

        var created = await LoadTemplateAsync(template.Id, cancellationToken);
        var createStats = await _recipientProtection.GetStatsAsync(template.Id, cancellationToken);
        return MapDetail(created!, createStats);
    }

    public async Task<EmailTemplateDetailDto> UpdateAsync(
        Guid id,
        UpsertEmailTemplateDto request,
        CancellationToken cancellationToken)
    {
        ValidateUpsert(request);
        await EnsureSignatureExistsAsync(request.SignatureId, cancellationToken);
        await EnsureEmailAccountExistsAsync(request.EmailAccountId, cancellationToken);

        var template = await _db.EmailTemplates
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (template is null)
        {
            throw new InvalidOperationException("Template was not found.");
        }

        template.Name = request.Name.Trim();
        template.Description = NormalizeOptional(request.Description);
        template.Subject = request.Subject.Trim();
        template.HtmlBody = request.HtmlBody.Trim();
        template.SignatureId = request.SignatureId;
        template.EmailAccountId = request.EmailAccountId;
        template.Category = NormalizeOptional(request.Category);
        template.WhatsAppTemplateName = NormalizeOptional(request.WhatsAppTemplateName);
        template.WhatsAppTemplateLanguage = NormalizeOptional(request.WhatsAppTemplateLanguage);
        template.WhatsAppHeaderParameter = NormalizeOptional(request.WhatsAppHeaderParameter);
        template.WhatsAppButtonSubType = NormalizeOptional(request.WhatsAppButtonSubType);
        template.WhatsAppButtonIndex = request.WhatsAppButtonIndex;
        template.WhatsAppButtonParameter = NormalizeOptional(request.WhatsAppButtonParameter);
        template.IsActive = request.IsActive;
        template.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await SyncTemplateAttachmentsAsync(template, request.AttachmentIds, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var updated = await LoadTemplateAsync(id, cancellationToken);
        var updateStats = await _recipientProtection.GetStatsAsync(id, cancellationToken);
        return MapDetail(updated!, updateStats);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await _db.EmailTemplates
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (template is null)
        {
            return;
        }

        var attachmentIds = new List<Guid>();
        foreach (var attachment in template.Attachments)
        {
            attachmentIds.Add(attachment.Id);
        }

        foreach (var attachmentId in attachmentIds)
        {
            await _attachmentStore.DeleteAsync(attachmentId, cancellationToken);
        }

        var linkedCampaigns = await _db.Campaigns
            .Where(x => x.TemplateId == id)
            .ToListAsync(cancellationToken);

        foreach (var campaign in linkedCampaigns)
        {
            campaign.TemplateId = null;
        }

        var historyRows = await _db.TemplateRecipientHistories
            .Where(x => x.TemplateId == id)
            .ToListAsync(cancellationToken);

        foreach (var history in historyRows)
        {
            _db.TemplateRecipientHistories.Remove(history);
        }

        var tracked = await _db.EmailTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (tracked is null)
        {
            return;
        }

        _db.EmailTemplates.Remove(tracked);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<EmailTemplateDetailDto> DuplicateAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await LoadTemplateAsync(id, cancellationToken);
        if (source is null)
        {
            throw new InvalidOperationException("Template was not found.");
        }

        var copy = new EmailTemplate
        {
            Name = BuildDuplicateName(source.Name),
            Description = source.Description,
            Subject = source.Subject,
            HtmlBody = source.HtmlBody,
            SignatureId = source.SignatureId,
            EmailAccountId = source.EmailAccountId,
            Category = source.Category,
            WhatsAppTemplateName = source.WhatsAppTemplateName,
            WhatsAppTemplateLanguage = source.WhatsAppTemplateLanguage,
            WhatsAppHeaderParameter = source.WhatsAppHeaderParameter,
            WhatsAppButtonSubType = source.WhatsAppButtonSubType,
            WhatsAppButtonIndex = source.WhatsAppButtonIndex,
            WhatsAppButtonParameter = source.WhatsAppButtonParameter,
            IsActive = source.IsActive,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.EmailTemplates.Add(copy);
        await _db.SaveChangesAsync(cancellationToken);

        var sourceAttachmentIds = new List<Guid>();
        foreach (var attachment in source.Attachments)
        {
            sourceAttachmentIds.Add(attachment.Id);
        }

        if (sourceAttachmentIds.Count > 0)
        {
            await _attachmentStore.AssignToTemplateAsync(sourceAttachmentIds, copy.Id, cancellationToken);
        }

        var created = await LoadTemplateAsync(copy.Id, cancellationToken);
        var duplicateStats = await _recipientProtection.GetStatsAsync(copy.Id, cancellationToken);
        return MapDetail(created!, duplicateStats);
    }

    private async Task SyncTemplateAttachmentsAsync(
        EmailTemplate template,
        List<Guid> attachmentIds,
        CancellationToken cancellationToken)
    {
        var desired = new HashSet<Guid>(attachmentIds);
        var existingIds = new List<Guid>();

        foreach (var attachment in template.Attachments)
        {
            existingIds.Add(attachment.Id);
            if (!desired.Contains(attachment.Id))
            {
                await _attachmentStore.DeleteAsync(attachment.Id, cancellationToken);
            }
        }

        var toAssign = new List<Guid>();
        foreach (var id in attachmentIds)
        {
            if (!existingIds.Contains(id))
            {
                toAssign.Add(id);
            }
        }

        if (toAssign.Count > 0)
        {
            await _attachmentStore.AssignToTemplateAsync(toAssign, template.Id, cancellationToken);
        }
    }

    private async Task<EmailTemplate?> LoadTemplateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.EmailTemplates
            .AsNoTracking()
            .Include(x => x.Signature)
            .Include(x => x.EmailAccount)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    private async Task EnsureSignatureExistsAsync(Guid? signatureId, CancellationToken cancellationToken)
    {
        if (signatureId is null)
        {
            return;
        }

        var exists = await _db.EmailSignatures
            .AsNoTracking()
            .AnyAsync(x => x.Id == signatureId.Value, cancellationToken);

        if (!exists)
        {
            throw new InvalidOperationException("Selected signature was not found.");
        }
    }

    private async Task EnsureEmailAccountExistsAsync(Guid? emailAccountId, CancellationToken cancellationToken)
    {
        if (emailAccountId is null)
        {
            return;
        }

        var exists = await _db.EmailAccounts
            .AsNoTracking()
            .AnyAsync(x => x.Id == emailAccountId.Value, cancellationToken);

        if (!exists)
        {
            throw new InvalidOperationException("Selected email account was not found.");
        }
    }

    private static void ValidateUpsert(UpsertEmailTemplateDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Template name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new InvalidOperationException("Subject is required.");
        }

        if (string.IsNullOrWhiteSpace(request.HtmlBody))
        {
            throw new InvalidOperationException("Email body is required.");
        }
    }

    private static EmailTemplateSummaryDto MapSummary(EmailTemplate template, TemplateStatsDto? stats)
    {
        return new EmailTemplateSummaryDto
        {
            Id = template.Id,
            Name = template.Name,
            Description = template.Description,
            Subject = template.Subject,
            SignatureId = template.SignatureId,
            SignatureName = template.Signature?.Name,
            EmailAccountId = template.EmailAccountId,
            EmailAccountName = template.EmailAccount?.DisplayName,
            EmailAccountAddress = template.EmailAccount?.EmailAddress,
            Category = template.Category,
            WhatsAppTemplateName = template.WhatsAppTemplateName,
            WhatsAppTemplateLanguage = template.WhatsAppTemplateLanguage,
            WhatsAppHeaderParameter = template.WhatsAppHeaderParameter,
            WhatsAppButtonSubType = template.WhatsAppButtonSubType,
            WhatsAppButtonIndex = template.WhatsAppButtonIndex,
            WhatsAppButtonParameter = template.WhatsAppButtonParameter,
            IsActive = template.IsActive,
            AttachmentCount = CountAttachments(template.Attachments),
            CreatedAtUtc = template.CreatedAtUtc,
            UpdatedAtUtc = template.UpdatedAtUtc,
            TotalCampaigns = stats?.TotalCampaigns ?? 0,
            UniqueRecipients = stats?.UniqueRecipients ?? 0,
            SuccessfullyContacted = stats?.SuccessfullyContacted ?? 0,
            FailedRecipients = stats?.Failed ?? 0,
            LastUsedAtUtc = stats?.LastUsedAtUtc
        };
    }

    private static EmailTemplateDetailDto MapDetail(EmailTemplate template, TemplateStatsDto? stats)
    {
        var summary = MapSummary(template, stats);
        return new EmailTemplateDetailDto
        {
            Id = summary.Id,
            Name = summary.Name,
            Description = summary.Description,
            Subject = summary.Subject,
            HtmlBody = template.HtmlBody,
            SignatureId = summary.SignatureId,
            SignatureName = summary.SignatureName,
            SignatureHtml = template.Signature?.HtmlContent,
            EmailAccountId = summary.EmailAccountId,
            EmailAccountName = summary.EmailAccountName,
            EmailAccountAddress = summary.EmailAccountAddress,
            Category = summary.Category,
            WhatsAppTemplateName = summary.WhatsAppTemplateName,
            WhatsAppTemplateLanguage = summary.WhatsAppTemplateLanguage,
            WhatsAppHeaderParameter = summary.WhatsAppHeaderParameter,
            WhatsAppButtonSubType = summary.WhatsAppButtonSubType,
            WhatsAppButtonIndex = summary.WhatsAppButtonIndex,
            WhatsAppButtonParameter = summary.WhatsAppButtonParameter,
            IsActive = summary.IsActive,
            AttachmentCount = summary.AttachmentCount,
            CreatedAtUtc = summary.CreatedAtUtc,
            UpdatedAtUtc = summary.UpdatedAtUtc,
            TotalCampaigns = summary.TotalCampaigns,
            UniqueRecipients = summary.UniqueRecipients,
            SuccessfullyContacted = summary.SuccessfullyContacted,
            FailedRecipients = summary.FailedRecipients,
            LastUsedAtUtc = summary.LastUsedAtUtc,
            Attachments = MapAttachments(template.Attachments)
        };
    }

    private static List<CampaignAttachmentDto> MapAttachments(ICollection<CampaignAttachment> attachments)
    {
        var result = new List<CampaignAttachmentDto>();
        foreach (var attachment in attachments)
        {
            result.Add(new CampaignAttachmentDto
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes
            });
        }

        return result;
    }

    private static int CountAttachments(ICollection<CampaignAttachment> attachments)
    {
        var count = 0;
        foreach (var _ in attachments)
        {
            count++;
        }

        return count;
    }

    private static bool MatchesSearch(EmailTemplate template, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var term = search.Trim();
        if (template.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (template.Subject.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (template.Signature?.Name is not null
            && template.Signature.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool MatchesStatus(EmailTemplate template, string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            return template.IsActive;
        }

        if (status.Equals("inactive", StringComparison.OrdinalIgnoreCase))
        {
            return !template.IsActive;
        }

        return true;
    }

    private static void SortByUpdatedDescending(List<EmailTemplate> templates)
    {
        templates.Sort(static (left, right) => right.UpdatedAtUtc.CompareTo(left.UpdatedAtUtc));
    }

    private static string BuildDuplicateName(string name)
    {
        return $"{name} (Copy)";
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
