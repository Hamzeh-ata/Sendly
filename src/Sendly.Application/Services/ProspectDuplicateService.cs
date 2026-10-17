using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class ProspectDuplicateLookupRequest
{
    public Guid ProjectId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string? Website { get; set; }

    public string? City { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? WhatsApp { get; set; }

    public string? SourceUrl { get; set; }

    public Guid? ExcludeProspectId { get; set; }

    public bool TrackExistingForMerge { get; set; } = true;
}

public class ProspectDuplicateService
{
    private readonly ISendlyDbContext _db;

    public ProspectDuplicateService(ISendlyDbContext db)
    {
        _db = db;
    }

    public async Task<ProspectDuplicateKind> EvaluateAsync(
        Guid projectId,
        string companyName,
        string? email,
        string? whatsApp,
        Guid? excludeProspectId,
        CancellationToken cancellationToken)
    {
        var match = await FindMatchAsync(
            new ProspectDuplicateLookupRequest
            {
                ProjectId = projectId,
                CompanyName = companyName,
                Email = email,
                WhatsApp = whatsApp,
                ExcludeProspectId = excludeProspectId,
                TrackExistingForMerge = false
            },
            cancellationToken);
        return match.Kind;
    }

    public Task<ProspectDuplicateMatch> FindMatchAsync(
        Guid projectId,
        string companyName,
        string? website,
        string? city,
        string? email,
        string? phone,
        string? whatsApp,
        string? sourceUrl,
        Guid? excludeProspectId,
        CancellationToken cancellationToken)
    {
        return FindMatchAsync(
            new ProspectDuplicateLookupRequest
            {
                ProjectId = projectId,
                CompanyName = companyName,
                Website = website,
                City = city,
                Email = email,
                Phone = phone,
                WhatsApp = whatsApp,
                SourceUrl = sourceUrl,
                ExcludeProspectId = excludeProspectId,
                TrackExistingForMerge = true
            },
            cancellationToken);
    }

    public async Task<ProspectDuplicateMatch> FindMatchAsync(
        ProspectDuplicateLookupRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedCompany = ProspectNormalizer.NormalizeCompanyName(request.CompanyName);
        var normalizedEmail = ProspectNormalizer.NormalizeEmail(request.Email);
        var normalizedPhone = ProspectNormalizer.NormalizePhone(request.Phone);
        var normalizedWhatsApp = ProspectNormalizer.NormalizeWhatsApp(request.WhatsApp)
            ?? normalizedPhone;
        var normalizedWebsite = ProspectNormalizer.NormalizeWebsite(request.Website)
            ?? ProspectNormalizer.NormalizeWebsite(request.SourceUrl);
        var normalizedCity = ProspectNormalizer.NormalizeCity(request.City);

        var doNotContact = await FindDoNotContactAsync(
            normalizedEmail,
            normalizedWhatsApp,
            request.ExcludeProspectId,
            cancellationToken);
        if (doNotContact is not null)
        {
            return ProspectDuplicateMatch.Skip(ProspectDuplicateKind.DoNotContact, doNotContact);
        }

        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            if (await IsEmailContactedAsync(normalizedEmail, cancellationToken))
            {
                var existingContacted = await FindSameProjectByEmailAsync(
                    request.ProjectId,
                    normalizedEmail,
                    request.ExcludeProspectId,
                    request.TrackExistingForMerge,
                    cancellationToken);
                return existingContacted is null
                    ? ProspectDuplicateMatch.Skip(ProspectDuplicateKind.EmailContacted)
                    : ProspectDuplicateMatch.Merge(ProspectDuplicateKind.EmailContacted, existingContacted);
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedWhatsApp))
        {
            if (await IsWhatsAppContactedAsync(normalizedWhatsApp, cancellationToken))
            {
                var existingContacted = await FindSameProjectByWhatsAppAsync(
                    request.ProjectId,
                    normalizedWhatsApp,
                    request.ExcludeProspectId,
                    request.TrackExistingForMerge,
                    cancellationToken);
                return existingContacted is null
                    ? ProspectDuplicateMatch.Skip(ProspectDuplicateKind.WhatsAppContacted)
                    : ProspectDuplicateMatch.Merge(ProspectDuplicateKind.WhatsAppContacted, existingContacted);
            }
        }

        var sameProjectEmail = await FindSameProjectByEmailAsync(
            request.ProjectId,
            normalizedEmail,
            request.ExcludeProspectId,
            request.TrackExistingForMerge,
            cancellationToken);
        if (sameProjectEmail is not null)
        {
            return ProspectDuplicateMatch.Merge(ProspectDuplicateKind.SameProject, sameProjectEmail);
        }

        var sameProjectWhatsApp = await FindSameProjectByWhatsAppAsync(
            request.ProjectId,
            normalizedWhatsApp,
            request.ExcludeProspectId,
            request.TrackExistingForMerge,
            cancellationToken);
        if (sameProjectWhatsApp is not null)
        {
            return ProspectDuplicateMatch.Merge(ProspectDuplicateKind.SameProject, sameProjectWhatsApp);
        }

        var sameProjectPhone = await FindSameProjectByPhoneAsync(
            request.ProjectId,
            normalizedPhone,
            request.ExcludeProspectId,
            request.TrackExistingForMerge,
            cancellationToken);
        if (sameProjectPhone is not null)
        {
            return ProspectDuplicateMatch.Merge(ProspectDuplicateKind.SamePhone, sameProjectPhone);
        }

        var sameProjectWebsite = await FindSameProjectByWebsiteAsync(
            request.ProjectId,
            normalizedWebsite,
            request.ExcludeProspectId,
            request.TrackExistingForMerge,
            cancellationToken);
        if (sameProjectWebsite is not null)
        {
            return ProspectDuplicateMatch.Merge(ProspectDuplicateKind.SameWebsite, sameProjectWebsite);
        }

        var crossProject = await FindCrossProjectContactAsync(
            request.ProjectId,
            normalizedEmail,
            normalizedWhatsApp,
            request.ExcludeProspectId,
            request.TrackExistingForMerge,
            cancellationToken);
        if (crossProject is not null)
        {
            return ProspectDuplicateMatch.Skip(ProspectDuplicateKind.CrossProject, crossProject);
        }

        var sameCompany = await FindSameProjectByCompanyAsync(
            request.ProjectId,
            normalizedCompany,
            normalizedCity,
            request.ExcludeProspectId,
            request.TrackExistingForMerge,
            cancellationToken);
        if (sameCompany.Prospect is not null)
        {
            return sameCompany.IsExact
                ? ProspectDuplicateMatch.Merge(ProspectDuplicateKind.SameCompany, sameCompany.Prospect)
                : ProspectDuplicateMatch.Review(ProspectDuplicateKind.PossibleDuplicate, sameCompany.Prospect);
        }

        return ProspectDuplicateMatch.None();
    }

    private async Task<Prospect?> FindDoNotContactAsync(
        string? normalizedEmail,
        string? normalizedWhatsApp,
        Guid? excludeProspectId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            var byEmail = await QueryProspects(excludeProspectId, track: true)
                .FirstOrDefaultAsync(
                    x => x.Status == ProspectStatus.DoNotContact && x.NormalizedEmail == normalizedEmail,
                    cancellationToken);
            if (byEmail is not null)
            {
                return byEmail;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedWhatsApp))
        {
            return await QueryProspects(excludeProspectId, track: true)
                .FirstOrDefaultAsync(
                    x => x.Status == ProspectStatus.DoNotContact && x.NormalizedWhatsApp == normalizedWhatsApp,
                    cancellationToken);
        }

        return null;
    }

    private async Task<bool> IsEmailContactedAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        if (await _db.OutreachContactHistories.AsNoTracking().AnyAsync(
                x => x.Channel == OutreachChannelType.Email && x.NormalizedValue == normalizedEmail,
                cancellationToken))
        {
            return true;
        }

        return await _db.TemplateRecipientHistories.AsNoTracking().AnyAsync(
            x => x.Status == TemplateRecipientContactStatus.Sent
                 && x.NormalizedRecipientEmail == normalizedEmail,
            cancellationToken);
    }

    private Task<bool> IsWhatsAppContactedAsync(string normalizedWhatsApp, CancellationToken cancellationToken)
    {
        return _db.OutreachContactHistories.AsNoTracking().AnyAsync(
            x => x.Channel == OutreachChannelType.WhatsApp && x.NormalizedValue == normalizedWhatsApp,
            cancellationToken);
    }

    private async Task<Prospect?> FindSameProjectByEmailAsync(
        Guid projectId,
        string? normalizedEmail,
        Guid? excludeProspectId,
        bool track,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return null;
        }

        return await QueryProspects(excludeProspectId, track)
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId && x.NormalizedEmail == normalizedEmail,
                cancellationToken);
    }

    private async Task<Prospect?> FindSameProjectByWhatsAppAsync(
        Guid projectId,
        string? normalizedWhatsApp,
        Guid? excludeProspectId,
        bool track,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedWhatsApp))
        {
            return null;
        }

        return await QueryProspects(excludeProspectId, track)
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId && x.NormalizedWhatsApp == normalizedWhatsApp,
                cancellationToken);
    }

    private async Task<Prospect?> FindSameProjectByPhoneAsync(
        Guid projectId,
        string? normalizedPhone,
        Guid? excludeProspectId,
        bool track,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return null;
        }

        return await QueryProspects(excludeProspectId, track)
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId
                     && (x.NormalizedPhone == normalizedPhone || x.NormalizedWhatsApp == normalizedPhone),
                cancellationToken);
    }

    private async Task<Prospect?> FindSameProjectByWebsiteAsync(
        Guid projectId,
        string? normalizedWebsite,
        Guid? excludeProspectId,
        bool track,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedWebsite))
        {
            return null;
        }

        return await QueryProspects(excludeProspectId, track)
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId && x.NormalizedWebsite == normalizedWebsite,
                cancellationToken);
    }

    private async Task<Prospect?> FindCrossProjectContactAsync(
        Guid projectId,
        string? normalizedEmail,
        string? normalizedWhatsApp,
        Guid? excludeProspectId,
        bool track,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            var byEmail = await QueryProspects(excludeProspectId, track)
                .FirstOrDefaultAsync(
                    x => x.ProjectId != projectId
                         && x.NormalizedEmail == normalizedEmail
                         && (x.Status == ProspectStatus.Contacted || x.OutreachCampaignId != null),
                    cancellationToken);
            if (byEmail is not null)
            {
                return byEmail;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedWhatsApp))
        {
            return await QueryProspects(excludeProspectId, track)
                .FirstOrDefaultAsync(
                    x => x.ProjectId != projectId
                         && x.NormalizedWhatsApp == normalizedWhatsApp
                         && (x.Status == ProspectStatus.Contacted || x.OutreachCampaignId != null),
                    cancellationToken);
        }

        return null;
    }

    private async Task<(Prospect? Prospect, bool IsExact)> FindSameProjectByCompanyAsync(
        Guid projectId,
        string normalizedCompany,
        string? normalizedCity,
        Guid? excludeProspectId,
        bool track,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedCompany))
        {
            return (null, false);
        }

        var exact = await QueryProspects(excludeProspectId, track)
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId && x.NormalizedCompanyName == normalizedCompany,
                cancellationToken);
        if (exact is not null)
        {
            return (exact, true);
        }

        var projectProspects = await QueryProspects(excludeProspectId, track: false)
            .Where(x => x.ProjectId == projectId)
            .Select(x => new { x.Id, x.CompanyName, x.NormalizedCompanyName, x.NormalizedCity })
            .ToListAsync(cancellationToken);

        foreach (var item in projectProspects)
        {
            if (!ProspectNormalizer.AreCompanyNamesLikelySame(normalizedCompany, item.NormalizedCompanyName)
                && !ProspectNormalizer.AreCompanyNamesLikelySame(normalizedCompany, item.CompanyName))
            {
                continue;
            }

            var cityMatches = string.IsNullOrWhiteSpace(normalizedCity)
                || string.IsNullOrWhiteSpace(item.NormalizedCity)
                || string.Equals(normalizedCity, item.NormalizedCity, StringComparison.OrdinalIgnoreCase);

            if (!cityMatches)
            {
                continue;
            }

            var tracked = track
                ? await _db.Prospects.FirstOrDefaultAsync(x => x.Id == item.Id, cancellationToken)
                : await _db.Prospects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.Id, cancellationToken);

            if (tracked is null)
            {
                continue;
            }

            var isExact = string.Equals(
                ProspectNormalizer.NormalizeCompanyName(tracked.CompanyName),
                normalizedCompany,
                StringComparison.Ordinal);
            return (tracked, isExact);
        }

        return (null, false);
    }

    private IQueryable<Prospect> QueryProspects(Guid? excludeProspectId, bool track)
    {
        IQueryable<Prospect> query = track ? _db.Prospects : _db.Prospects.AsNoTracking();
        if (excludeProspectId is not null)
        {
            query = query.Where(x => x.Id != excludeProspectId);
        }

        return query;
    }
}
