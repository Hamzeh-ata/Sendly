using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services;

public class EmailOutreachChannel : IOutreachChannel
{
    private readonly ISendlyDbContext _db;
    private readonly CampaignService _campaignService;
    private readonly IAiResearchService _ai;
    private readonly ILogger<EmailOutreachChannel> _logger;

    public EmailOutreachChannel(
        ISendlyDbContext db,
        CampaignService campaignService,
        IAiResearchService ai,
        ILogger<EmailOutreachChannel> logger)
    {
        _db = db;
        _campaignService = campaignService;
        _ai = ai;
        _logger = logger;
    }

    public OutreachChannelType Channel => OutreachChannelType.Email;

    public async Task<OutreachPrepareResult> PrepareAsync(
        OutreachPrepareRequest request,
        CancellationToken cancellationToken)
    {
        var result = new OutreachPrepareResult
        {
            Channel = OutreachChannelType.Email,
            SelectedCount = request.ProspectIds.Count
        };

        if (request.ProspectIds.Count == 0)
        {
            throw new InvalidOperationException("Select at least one prospect.");
        }

        var prospects = await _db.Prospects
            .Where(x => x.ProjectId == request.ProjectId && request.ProspectIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (prospects.Count == 0)
        {
            throw new InvalidOperationException("No matching prospects were found for this project.");
        }

        var templateId = await ResolveTemplateIdAsync(request, prospects, cancellationToken);
        var template = await _db.EmailTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == templateId, cancellationToken);
        if (template is null || !template.IsActive)
        {
            throw new InvalidOperationException("Email template was not found or is inactive.");
        }

        var personalized = new List<CreateCampaignRecipientDto>();
        var prepared = new List<Prospect>();
        foreach (var prospect in prospects)
        {
            if (prospect.Status == ProspectStatus.DoNotContact)
            {
                result.SkippedInvalidCount++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(prospect.Email))
            {
                result.SkippedInvalidCount++;
                continue;
            }

            var parsed = EmailAddressParser.Parse(prospect.Email);
            if (parsed.ValidEmails.Count == 0)
            {
                result.SkippedInvalidCount++;
                continue;
            }

            var values = await EnsurePersonalizationAsync(prospect, cancellationToken);
            personalized.Add(new CreateCampaignRecipientDto
            {
                Email = parsed.ValidEmails[0],
                ProspectId = prospect.Id,
                DisplayName = prospect.ContactName,
                CompanyName = prospect.CompanyName,
                Personalization = values
            });
            prepared.Add(prospect);
        }

        if (personalized.Count == 0)
        {
            throw new InvalidOperationException("No valid email prospects were available for outreach.");
        }

        var attachmentIds = await LoadTemplateAttachmentIdsAsync(template.Id, cancellationToken);
        var campaign = await _campaignService.CreateDraftAsync(
            new CreateCampaignRequestDto
            {
                Subject = template.Subject,
                HtmlBody = template.HtmlBody,
                PersonalizedRecipients = personalized,
                AttachmentIds = attachmentIds,
                TemplateId = template.Id,
                SignatureId = request.SignatureId ?? template.SignatureId,
                EmailAccountId = request.EmailAccountId ?? template.EmailAccountId,
                AllowAlreadyContacted = request.AllowAlreadyContacted,
                DelayMilliseconds = request.DelayMilliseconds,
                BatchSize = request.BatchSize,
                MaxRetryCount = request.MaxRetryCount,
                IdempotencyKey = $"outreach:{request.ProjectId}:{template.Id}:{Guid.NewGuid():N}"
            },
            cancellationToken);

        foreach (var prospect in prepared)
        {
            prospect.OutreachCampaignId = campaign.Id;
            prospect.Status = ProspectStatus.Contacted;
            prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(prospect.NormalizedEmail))
            {
                _db.OutreachContactHistories.Add(new OutreachContactHistory
                {
                    Channel = OutreachChannelType.Email,
                    ContactValue = prospect.Email ?? prospect.NormalizedEmail,
                    NormalizedValue = prospect.NormalizedEmail,
                    ProspectId = prospect.Id,
                    ProjectId = prospect.ProjectId,
                    CampaignId = campaign.Id,
                    TemplateId = template.Id,
                    ContactedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Campaign created {CampaignId} from email outreach", campaign.Id);

        result.CampaignId = campaign.Id;
        result.TemplateId = template.Id;
        result.TemplateName = template.Name;
        result.PreparedCount = prepared.Count;
        result.PreparedProspectIds = prepared.Select(x => x.Id).ToList();
        result.SkippedDuplicateCount = Math.Max(0, request.ProspectIds.Count - prepared.Count - result.SkippedInvalidCount);
        if (campaign.TotalRecipients < personalized.Count)
        {
            result.Warnings.Add("Some recipients were filtered by existing template duplicate protection.");
            result.SkippedDuplicateCount += personalized.Count - campaign.TotalRecipients;
        }

        return result;
    }

    private async Task<Dictionary<string, string>> EnsurePersonalizationAsync(
        Prospect prospect,
        CancellationToken cancellationToken)
    {
        var existing = PersonalizationSerializer.Deserialize(prospect.PersonalizationJson);
        if (existing.Count > 0)
        {
            return existing;
        }

        var extracted = await _ai.ExtractPersonalizationAsync(
            new AiClassificationRequest
            {
                CompanyName = prospect.CompanyName,
                ContactName = prospect.ContactName,
                ContactRole = prospect.ContactRole,
                Industry = prospect.Industry,
                City = prospect.City,
                Country = prospect.Country,
                Email = prospect.Email
            },
            cancellationToken);

        prospect.PersonalizationJson = PersonalizationSerializer.Serialize(extracted.Values);
        return extracted.Values;
    }

    private async Task<List<Guid>> LoadTemplateAttachmentIdsAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        return await _db.CampaignAttachments.AsNoTracking()
            .Where(x => x.TemplateId == templateId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<Guid> ResolveTemplateIdAsync(
        OutreachPrepareRequest request,
        List<Prospect> prospects,
        CancellationToken cancellationToken)
    {
        if (request.TemplateId is not null)
        {
            return request.TemplateId.Value;
        }

        if (request.UseAiRecommendedTemplate)
        {
            foreach (var prospect in prospects)
            {
                if (prospect.RecommendedTemplateId is not null)
                {
                    return prospect.RecommendedTemplateId.Value;
                }
            }
        }

        var linked = await _db.ProjectTemplates.AsNoTracking()
            .Where(x => x.ProjectId == request.ProjectId && x.Channel == OutreachChannelType.Email)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.TemplateId)
            .FirstOrDefaultAsync(cancellationToken);

        if (linked == Guid.Empty)
        {
            throw new InvalidOperationException("No email template is linked to this project.");
        }

        return linked;
    }
}
