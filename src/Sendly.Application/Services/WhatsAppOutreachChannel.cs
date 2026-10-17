using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services;

public class WhatsAppOutreachChannel : IOutreachChannel
{
    private readonly ISendlyDbContext _db;
    private readonly IWhatsAppSender _whatsAppSender;
    private readonly IAiResearchService _ai;
    private readonly ILogger<WhatsAppOutreachChannel> _logger;

    public WhatsAppOutreachChannel(
        ISendlyDbContext db,
        IWhatsAppSender whatsAppSender,
        IAiResearchService ai,
        ILogger<WhatsAppOutreachChannel> logger)
    {
        _db = db;
        _whatsAppSender = whatsAppSender;
        _ai = ai;
        _logger = logger;
    }

    public OutreachChannelType Channel => OutreachChannelType.WhatsApp;

    public async Task<OutreachPrepareResult> PrepareAsync(
        OutreachPrepareRequest request,
        CancellationToken cancellationToken)
    {
        var result = new OutreachPrepareResult
        {
            Channel = OutreachChannelType.WhatsApp,
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

        Guid? templateId = request.TemplateId;
        EmailTemplate? template = null;
        if (templateId is null)
        {
            templateId = await _db.ProjectTemplates.AsNoTracking()
                .Where(x => x.ProjectId == request.ProjectId && x.Channel == OutreachChannelType.WhatsApp)
                .OrderBy(x => x.SortOrder)
                .Select(x => (Guid?)x.TemplateId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (templateId is not null)
        {
            template = await _db.EmailTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == templateId, cancellationToken);
        }

        var prepared = new List<Guid>();
        var sendEnabled = _whatsAppSender.IsConfigured;
        foreach (var prospect in prospects)
        {
            if (prospect.Status == ProspectStatus.DoNotContact)
            {
                result.SkippedInvalidCount++;
                continue;
            }

            var normalized = ProspectNormalizer.NormalizeWhatsApp(prospect.WhatsApp);
            if (normalized is null)
            {
                result.SkippedInvalidCount++;
                continue;
            }

            var alreadyContacted = await _db.OutreachContactHistories.AsNoTracking().AnyAsync(
                x => x.Channel == OutreachChannelType.WhatsApp && x.NormalizedValue == normalized,
                cancellationToken);
            if (alreadyContacted && !request.AllowAlreadyContacted)
            {
                result.SkippedDuplicateCount++;
                continue;
            }

            if (sendEnabled)
            {
                var messageBody = await BuildMessageBodyAsync(prospect, template, cancellationToken);
                var templateParams = BuildTemplateParameters(prospect);
                var sendResult = await _whatsAppSender.SendAsync(
                    new WhatsAppSendRequest
                    {
                        ToNumber = normalized,
                        MessageBody = messageBody,
                        TemplateName = ResolveWhatsAppTemplateName(template),
                        TemplateLanguage = ResolveWhatsAppTemplateLanguage(template),
                        TemplateBodyParameters = templateParams,
                        TemplateHeaderParameter = ResolveHeaderParameter(template, prospect),
                        TemplateButtonSubType = ResolveButtonSubType(template),
                        TemplateButtonIndex = template?.WhatsAppButtonIndex,
                        TemplateButtonParameter = ResolveButtonParameter(template, prospect),
                        PreferTemplate = !string.IsNullOrWhiteSpace(ResolveWhatsAppTemplateName(template))
                    },
                    cancellationToken);

                if (!sendResult.Success)
                {
                    result.Warnings.Add($"{prospect.CompanyName}: {sendResult.Message}");
                    result.SkippedInvalidCount++;
                    continue;
                }

                prospect.Status = ProspectStatus.Contacted;
                prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
                _db.OutreachContactHistories.Add(new OutreachContactHistory
                {
                    Channel = OutreachChannelType.WhatsApp,
                    ContactValue = prospect.WhatsApp ?? normalized,
                    NormalizedValue = normalized,
                    ProspectId = prospect.Id,
                    ProjectId = prospect.ProjectId,
                    TemplateId = templateId,
                    ContactedAtUtc = DateTimeOffset.UtcNow
                });
            }
            else
            {
                prospect.Status = ProspectStatus.ReadyForOutreach;
                prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            prepared.Add(prospect.Id);
        }

        if (prepared.Count == 0)
        {
            throw new InvalidOperationException("No valid WhatsApp prospects were available for preparation.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "WhatsApp outreach prepared for project {ProjectId} count {Count} sendEnabled {SendEnabled}",
            request.ProjectId,
            prepared.Count,
            sendEnabled);

        result.TemplateId = templateId;
        result.TemplateName = template?.Name;
        result.PreparedCount = prepared.Count;
        result.PreparedProspectIds = prepared;
        if (!sendEnabled)
        {
            result.Warnings.Add("WhatsApp Business API is not configured. Prospects were validated and marked ready for outreach only.");
        }

        return result;
    }

    private async Task<string> BuildMessageBodyAsync(
        Prospect prospect,
        EmailTemplate? template,
        CancellationToken cancellationToken)
    {
        var values = PersonalizationSerializer.Deserialize(prospect.PersonalizationJson);
        if (values.Count == 0)
        {
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
            values = extracted.Values;
            prospect.PersonalizationJson = PersonalizationSerializer.Serialize(values);
        }

        var mergeValues = TemplateMergeRenderer.BuildValues(
            prospect.Email,
            prospect.ContactName,
            prospect.CompanyName,
            prospect.Industry,
            prospect.City,
            prospect.Country,
            prospect.ContactRole,
            values);

        if (template is not null && !string.IsNullOrWhiteSpace(template.HtmlBody))
        {
            var plain = HtmlToPlainTextConverter.Convert(template.HtmlBody);
            return TemplateMergeRenderer.Render(plain, mergeValues);
        }

        var contact = string.IsNullOrWhiteSpace(prospect.ContactName) ? "there" : prospect.ContactName;
        return $"Hello {contact}, we would like to connect with {prospect.CompanyName}.";
    }

    private static string? ResolveWhatsAppTemplateName(EmailTemplate? template)
    {
        if (template is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(template.WhatsAppTemplateName)
            ? null
            : template.WhatsAppTemplateName.Trim();
    }

    private static string? ResolveWhatsAppTemplateLanguage(EmailTemplate? template)
    {
        if (template is null || string.IsNullOrWhiteSpace(template.WhatsAppTemplateLanguage))
        {
            return null;
        }

        return template.WhatsAppTemplateLanguage.Trim();
    }

    private static string? ResolveHeaderParameter(EmailTemplate? template, Prospect prospect)
    {
        if (template is null || string.IsNullOrWhiteSpace(template.WhatsAppHeaderParameter))
        {
            return null;
        }

        return RenderTemplateToken(template.WhatsAppHeaderParameter.Trim(), prospect);
    }

    private static string? ResolveButtonSubType(EmailTemplate? template)
    {
        if (template is null || string.IsNullOrWhiteSpace(template.WhatsAppButtonSubType))
        {
            return null;
        }

        return template.WhatsAppButtonSubType.Trim();
    }

    private static string? ResolveButtonParameter(EmailTemplate? template, Prospect prospect)
    {
        if (template is null || string.IsNullOrWhiteSpace(template.WhatsAppButtonParameter))
        {
            return null;
        }

        return RenderTemplateToken(template.WhatsAppButtonParameter.Trim(), prospect);
    }

    private static string RenderTemplateToken(string value, Prospect prospect)
    {
        return value
            .Replace("{{ContactName}}", prospect.ContactName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{{CompanyName}}", prospect.CompanyName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{ProspectId}}", prospect.Id.ToString("N"), StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildTemplateParameters(Prospect prospect)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(prospect.ContactName))
        {
            values.Add(prospect.ContactName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(prospect.CompanyName))
        {
            values.Add(prospect.CompanyName.Trim());
        }

        return values;
    }
}
