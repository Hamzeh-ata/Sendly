using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services;

public class ProspectService
{
    private readonly ISendlyDbContext _db;
    private readonly IAiResearchService _ai;
    private readonly ProspectDuplicateService _duplicates;
    private readonly IEnumerable<IOutreachChannel> _channels;
    private readonly ILogger<ProspectService> _logger;

    public ProspectService(
        ISendlyDbContext db,
        IAiResearchService ai,
        ProspectDuplicateService duplicates,
        IEnumerable<IOutreachChannel> channels,
        ILogger<ProspectService> logger)
    {
        _db = db;
        _ai = ai;
        _duplicates = duplicates;
        _channels = channels;
        _logger = logger;
    }

    public async Task<List<ProspectSummaryDto>> ListAsync(
        ProspectFilterDto filter,
        CancellationToken cancellationToken)
    {
        var query = _db.Prospects.AsNoTracking().AsQueryable();
        query = ApplyFilters(query, filter);

        var prospects = await query.ToListAsync(cancellationToken);
        prospects = prospects.OrderByDescending(x => x.CreatedAtUtc).ToList();
        var projectIds = prospects.Select(x => x.ProjectId).Distinct().ToList();
        var projectNames = await _db.OutreachProjects.AsNoTracking()
            .Where(x => projectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var templateIds = prospects
            .Where(x => x.RecommendedTemplateId != null)
            .Select(x => x.RecommendedTemplateId!.Value)
            .Distinct()
            .ToList();
        var templateNames = await _db.EmailTemplates.AsNoTracking()
            .Where(x => templateIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var result = new List<ProspectSummaryDto>();
        foreach (var prospect in prospects)
        {
            projectNames.TryGetValue(prospect.ProjectId, out var projectName);
            string? templateName = null;
            if (prospect.RecommendedTemplateId is not null)
            {
                templateNames.TryGetValue(prospect.RecommendedTemplateId.Value, out templateName);
            }

            result.Add(MapSummary(prospect, projectName, templateName));
        }

        return result;
    }

    public async Task<ProspectDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var prospect = await _db.Prospects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (prospect is null)
        {
            return null;
        }

        var projectName = await _db.OutreachProjects.AsNoTracking()
            .Where(x => x.Id == prospect.ProjectId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);

        string? templateName = null;
        if (prospect.RecommendedTemplateId is not null)
        {
            templateName = await _db.EmailTemplates.AsNoTracking()
                .Where(x => x.Id == prospect.RecommendedTemplateId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var history = await LoadContactHistoryAsync(prospect, cancellationToken);
        var detail = MapDetail(prospect, projectName, templateName);
        detail.ContactHistory = history;
        return detail;
    }

    public async Task<ProspectDetailDto> CreateAsync(CreateProspectDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            throw new InvalidOperationException("Company name is required.");
        }

        var project = await _db.OutreachProjects
            .FirstOrDefaultAsync(x => x.Id == request.ProjectId, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        project.Keywords = await _db.OutreachProjectKeywords
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.Countries = await _db.OutreachProjectCountries
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.Industries = await _db.OutreachProjectIndustries
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.TargetRoles = await _db.OutreachProjectTargetRoles
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.Templates = await _db.ProjectTemplates
            .Include(x => x.Template)
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(request.SourceUrl) && string.IsNullOrWhiteSpace(request.SourceName))
        {
            throw new InvalidOperationException("Source information is required.");
        }

        var email = NormalizeEmailOrNull(request.Email);
        var whatsApp = ProspectNormalizer.NormalizeWhatsApp(request.WhatsApp);
        var match = await _duplicates.FindMatchAsync(
            new ProspectDuplicateLookupRequest
            {
                ProjectId = project.Id,
                CompanyName = request.CompanyName,
                Website = request.Website,
                City = request.City,
                Email = email,
                Phone = request.Phone,
                WhatsApp = request.WhatsApp,
                SourceUrl = request.SourceUrl,
                ExcludeProspectId = null,
                TrackExistingForMerge = true
            },
            cancellationToken);

        if (match.ShouldSkip
            || match.Kind == ProspectDuplicateKind.DoNotContact
            || (project.DuplicateHandling == DuplicateHandlingMode.Exclude
                && match.Kind is ProspectDuplicateKind.EmailContacted
                    or ProspectDuplicateKind.WhatsAppContacted
                    or ProspectDuplicateKind.CrossProject))
        {
            throw new InvalidOperationException($"Prospect was excluded as duplicate ({match.Kind}).");
        }

        var classification = BuildClassification(project, request, email, whatsApp);
        var qualification = await _ai.QualifyAsync(classification, cancellationToken);
        if (match.NeedsReview)
        {
            qualification.NeedsReview = true;
        }

        var recommendation = await RecommendAsync(project, classification, qualification, cancellationToken);
        var status = ResolveManualStatus(qualification, project.MinQualificationScore, match, project.DuplicateHandling);

        if (match.ShouldMerge && match.ExistingProspect is not null)
        {
            var existing = match.ExistingProspect;
            ProspectMergeHelper.MergeCandidateInto(
                existing,
                new ProspectMergeInput
                {
                    CompanyName = request.CompanyName.Trim(),
                    Website = TrimOrNull(request.Website),
                    Country = TrimOrNull(request.Country),
                    City = TrimOrNull(request.City),
                    Industry = TrimOrNull(qualification.Industry) ?? TrimOrNull(request.Industry),
                    CompanyDescription = TrimOrNull(request.CompanyDescription),
                    CompanySize = TrimOrNull(qualification.CompanySize) ?? TrimOrNull(request.CompanySize),
                    ContactName = TrimOrNull(request.ContactName),
                    ContactRole = TrimOrNull(qualification.ContactRole) ?? TrimOrNull(request.ContactRole),
                    Email = email,
                    Phone = TrimOrNull(request.Phone),
                    WhatsApp = TrimOrNull(request.WhatsApp),
                    LinkedInUrl = TrimOrNull(request.LinkedInUrl),
                    SourceUrl = TrimOrNull(request.SourceUrl),
                    SourceType = request.SourceType,
                    SourceName = TrimOrNull(request.SourceName) ?? "Manual",
                    ResearchNotes = TrimOrNull(request.ResearchNotes)
                });
            ProspectMergeHelper.ApplyQualificationMerge(
                existing,
                TrimOrNull(qualification.Summary),
                qualification.QualificationScore,
                qualification.EmailConfidenceScore,
                qualification.WhatsAppConfidenceScore,
                status,
                status is ProspectStatus.Qualified or ProspectStatus.ReadyForOutreach,
                match.Kind,
                null,
                recommendation?.TemplateId,
                recommendation?.Reason,
                recommendation?.Confidence);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Merged manual prospect into {ProspectId}", existing.Id);
            return (await GetAsync(existing.Id, cancellationToken))!;
        }

        var prospect = new Prospect
        {
            ProjectId = project.Id,
            CompanyName = request.CompanyName.Trim(),
            NormalizedCompanyName = ProspectNormalizer.NormalizeCompanyName(request.CompanyName),
            Website = TrimOrNull(request.Website),
            NormalizedWebsite = ProspectNormalizer.NormalizeWebsite(request.Website)
                ?? ProspectNormalizer.NormalizeWebsite(request.SourceUrl),
            Country = TrimOrNull(request.Country),
            City = TrimOrNull(request.City),
            NormalizedCity = ProspectNormalizer.NormalizeCity(request.City),
            Industry = TrimOrNull(qualification.Industry) ?? TrimOrNull(request.Industry),
            CompanyDescription = TrimOrNull(request.CompanyDescription),
            CompanySize = TrimOrNull(qualification.CompanySize) ?? TrimOrNull(request.CompanySize),
            ContactName = TrimOrNull(request.ContactName),
            ContactRole = TrimOrNull(qualification.ContactRole) ?? TrimOrNull(request.ContactRole),
            Email = email,
            NormalizedEmail = email is null ? null : EmailAddressParser.Normalize(email),
            Phone = TrimOrNull(request.Phone),
            NormalizedPhone = ProspectNormalizer.NormalizePhone(request.Phone),
            WhatsApp = TrimOrNull(request.WhatsApp),
            NormalizedWhatsApp = whatsApp,
            LinkedInUrl = TrimOrNull(request.LinkedInUrl),
            SourceUrl = TrimOrNull(request.SourceUrl),
            SourceUrlsJson = ProspectSourceUrlStore.Serialize([request.SourceUrl]),
            SourceType = request.SourceType,
            SourceName = TrimOrNull(request.SourceName) ?? "Manual",
            ResearchNotes = TrimOrNull(request.ResearchNotes),
            QualificationSummary = TrimOrNull(qualification.Summary),
            QualificationScore = qualification.QualificationScore,
            EmailConfidenceScore = qualification.EmailConfidenceScore,
            WhatsAppConfidenceScore = qualification.WhatsAppConfidenceScore,
            IsQualified = status is ProspectStatus.Qualified or ProspectStatus.ReadyForOutreach,
            Status = status,
            DuplicateKind = match.Kind,
            RecommendedTemplateId = recommendation?.TemplateId,
            RecommendedTemplateReason = recommendation?.Reason,
            RecommendedTemplateConfidence = recommendation?.Confidence
        };

        _db.Prospects.Add(prospect);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Candidate discovered manually {ProspectId}", prospect.Id);
        return (await GetAsync(prospect.Id, cancellationToken))!;
    }

    public async Task ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var prospect = await RequireProspectAsync(id, cancellationToken);
        prospect.Status = ProspectStatus.ReadyForOutreach;
        prospect.IsQualified = true;
        prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Prospect qualified {ProspectId}", id);
    }

    public async Task RejectAsync(Guid id, CancellationToken cancellationToken)
    {
        var prospect = await RequireProspectAsync(id, cancellationToken);
        prospect.Status = ProspectStatus.Rejected;
        prospect.IsQualified = false;
        prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Prospect rejected {ProspectId}", id);
    }

    public async Task MarkDoNotContactAsync(Guid id, CancellationToken cancellationToken)
    {
        var prospect = await RequireProspectAsync(id, cancellationToken);
        prospect.Status = ProspectStatus.DoNotContact;
        prospect.IsQualified = false;
        prospect.DuplicateKind = ProspectDuplicateKind.DoNotContact;
        prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RecommendTemplateResponseDto> RecommendTemplateAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var prospect = await RequireProspectAsync(id, cancellationToken);
        var project = await _db.OutreachProjects
            .Include(x => x.Templates)
            .ThenInclude(x => x.Template)
            .FirstOrDefaultAsync(x => x.Id == prospect.ProjectId, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        var result = await _ai.RecommendTemplateAsync(
            new AiTemplateRecommendationRequest
            {
                Industry = prospect.Industry,
                CompanySize = prospect.CompanySize,
                CompanyName = prospect.CompanyName,
                ContactRole = prospect.ContactRole,
                Country = prospect.Country,
                Templates = project.Templates.Select(x => new AiTemplateOption
                {
                    TemplateId = x.TemplateId,
                    Name = x.Template?.Name ?? x.TemplateId.ToString(),
                    Category = x.Template?.Category,
                    MatchIndustry = x.MatchIndustry,
                    MatchCompanySize = x.MatchCompanySize,
                    MatchNotes = x.MatchNotes
                }).ToList()
            },
            cancellationToken);

        prospect.RecommendedTemplateId = result.TemplateId;
        prospect.RecommendedTemplateReason = result.Reason;
        prospect.RecommendedTemplateConfidence = result.Confidence;
        prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        string? templateName = null;
        if (result.TemplateId is not null)
        {
            templateName = await _db.EmailTemplates.AsNoTracking()
                .Where(x => x.Id == result.TemplateId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new RecommendTemplateResponseDto
        {
            TemplateId = result.TemplateId,
            TemplateName = templateName,
            Reason = result.Reason,
            Confidence = result.Confidence
        };
    }

    public async Task<PrepareOutreachResponseDto> PrepareOutreachAsync(
        Guid projectId,
        PrepareOutreachRequestDto request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Campaign preparation started for project {ProjectId}", projectId);
        var channel = ResolveChannel(request.Channel);
        var result = await channel.PrepareAsync(
            new OutreachPrepareRequest
            {
                ProjectId = projectId,
                ProspectIds = request.ProspectIds,
                Channel = request.Channel,
                TemplateId = request.TemplateId,
                UseAiRecommendedTemplate = request.UseAiRecommendedTemplate,
                EmailAccountId = request.EmailAccountId,
                SignatureId = request.SignatureId,
                AllowAlreadyContacted = request.AllowAlreadyContacted,
                DelayMilliseconds = request.DelayMilliseconds,
                BatchSize = request.BatchSize,
                MaxRetryCount = request.MaxRetryCount
            },
            cancellationToken);

        _logger.LogInformation(
            "Campaign created {CampaignId} prepared {Count}",
            result.CampaignId,
            result.PreparedCount);

        return new PrepareOutreachResponseDto
        {
            Channel = result.Channel,
            CampaignId = result.CampaignId,
            TemplateId = result.TemplateId,
            TemplateName = result.TemplateName,
            SelectedCount = result.SelectedCount,
            PreparedCount = result.PreparedCount,
            SkippedDuplicateCount = result.SkippedDuplicateCount,
            SkippedInvalidCount = result.SkippedInvalidCount,
            Warnings = result.Warnings,
            PreparedProspectIds = result.PreparedProspectIds
        };
    }

    private IOutreachChannel ResolveChannel(OutreachChannelType channel)
    {
        foreach (var item in _channels)
        {
            if (item.Channel == channel)
            {
                return item;
            }
        }

        throw new InvalidOperationException($"Outreach channel '{channel}' is not available.");
    }

    private async Task<Prospect> RequireProspectAsync(Guid id, CancellationToken cancellationToken)
    {
        var prospect = await _db.Prospects.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (prospect is null)
        {
            throw new InvalidOperationException("Prospect was not found.");
        }

        return prospect;
    }

    private async Task<List<OutreachContactHistoryDto>> LoadContactHistoryAsync(
        Prospect prospect,
        CancellationToken cancellationToken)
    {
        var result = new List<OutreachContactHistoryDto>();
        if (!string.IsNullOrWhiteSpace(prospect.NormalizedEmail))
        {
            var emailHistory = await _db.OutreachContactHistories.AsNoTracking()
                .Where(x => x.Channel == OutreachChannelType.Email && x.NormalizedValue == prospect.NormalizedEmail)
                .ToListAsync(cancellationToken);
            emailHistory = emailHistory.OrderByDescending(x => x.ContactedAtUtc).ToList();
            foreach (var item in emailHistory)
            {
                result.Add(MapHistory(item));
            }

            var templateHistory = await _db.TemplateRecipientHistories.AsNoTracking()
                .Where(x => x.Status == TemplateRecipientContactStatus.Sent
                            && x.NormalizedRecipientEmail == prospect.NormalizedEmail)
                .ToListAsync(cancellationToken);
            templateHistory = templateHistory.OrderByDescending(x => x.SentAtUtc).ToList();
            foreach (var item in templateHistory)
            {
                result.Add(new OutreachContactHistoryDto
                {
                    Id = item.Id,
                    Channel = OutreachChannelType.Email,
                    ContactValue = item.RecipientEmail,
                    CampaignId = item.CampaignId,
                    TemplateId = item.TemplateId,
                    ContactedAtUtc = item.SentAtUtc ?? DateTimeOffset.UtcNow
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(prospect.NormalizedWhatsApp))
        {
            var whatsAppHistory = await _db.OutreachContactHistories.AsNoTracking()
                .Where(x => x.Channel == OutreachChannelType.WhatsApp && x.NormalizedValue == prospect.NormalizedWhatsApp)
                .ToListAsync(cancellationToken);
            whatsAppHistory = whatsAppHistory.OrderByDescending(x => x.ContactedAtUtc).ToList();
            foreach (var item in whatsAppHistory)
            {
                result.Add(MapHistory(item));
            }
        }

        return result.OrderByDescending(x => x.ContactedAtUtc).ToList();
    }

    private static IQueryable<Prospect> ApplyFilters(IQueryable<Prospect> query, ProspectFilterDto filter)
    {
        if (filter.ProjectId is not null)
        {
            query = query.Where(x => x.ProjectId == filter.ProjectId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x =>
                x.CompanyName.Contains(term)
                || (x.ContactName != null && x.ContactName.Contains(term))
                || (x.Email != null && x.Email.Contains(term))
                || (x.WhatsApp != null && x.WhatsApp.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Country))
        {
            query = query.Where(x => x.Country != null && x.Country.Contains(filter.Country));
        }

        if (!string.IsNullOrWhiteSpace(filter.Industry))
        {
            query = query.Where(x => x.Industry != null && x.Industry.Contains(filter.Industry));
        }

        query = ApplyContactMethodFilters(query, filter.ContactMethods);
        query = ApplyStatusFilters(query, filter.Statuses);
        query = ApplyQualificationFilters(query, filter.Qualifications);
        query = ApplyDuplicateFilters(query, filter.Duplicates);

        return query;
    }

    private static IQueryable<Prospect> ApplyContactMethodFilters(
        IQueryable<Prospect> query,
        string[]? contactMethods)
    {
        var methods = NormalizeFilterValues(contactMethods);
        if (methods.Count == 0)
        {
            return query;
        }

        var includeEmail = ContainsNormalized(methods, "email");
        var includeWhatsApp = ContainsNormalized(methods, "whatsapp");
        if (!includeEmail && !includeWhatsApp)
        {
            return query;
        }

        return query.Where(x =>
            (includeEmail && x.Email != null && x.Email != string.Empty)
            || (includeWhatsApp && x.WhatsApp != null && x.WhatsApp != string.Empty));
    }

    private static IQueryable<Prospect> ApplyStatusFilters(
        IQueryable<Prospect> query,
        string[]? statuses)
    {
        var parsed = ParseEnumValues<ProspectStatus>(statuses);
        if (parsed.Count == 0)
        {
            return query;
        }

        return query.Where(x => parsed.Contains(x.Status));
    }

    private static IQueryable<Prospect> ApplyQualificationFilters(
        IQueryable<Prospect> query,
        string[]? qualifications)
    {
        var values = NormalizeFilterValues(qualifications);
        if (values.Count == 0)
        {
            return query;
        }

        var includeQualified = ContainsNormalized(values, "qualified");
        var includeUnqualified = ContainsNormalized(values, "unqualified");
        if (includeQualified && includeUnqualified)
        {
            return query;
        }

        if (includeQualified)
        {
            return query.Where(x => x.IsQualified);
        }

        if (includeUnqualified)
        {
            return query.Where(x => !x.IsQualified);
        }

        return query;
    }

    private static IQueryable<Prospect> ApplyDuplicateFilters(
        IQueryable<Prospect> query,
        string[]? duplicates)
    {
        var values = NormalizeFilterValues(duplicates);
        if (values.Count == 0)
        {
            return query;
        }

        var kinds = new List<ProspectDuplicateKind>();
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (value == "new" || value == "none")
            {
                kinds.Add(ProspectDuplicateKind.None);
                continue;
            }

            if (Enum.TryParse<ProspectDuplicateKind>(value, true, out var kind))
            {
                kinds.Add(kind);
            }
        }

        if (kinds.Count == 0)
        {
            return query;
        }

        return query.Where(x => kinds.Contains(x.DuplicateKind));
    }

    private static List<string> NormalizeFilterValues(string[]? values)
    {
        var result = new List<string>();
        if (values is null)
        {
            return result;
        }

        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result.Add(value.Trim().ToLowerInvariant());
        }

        return result;
    }

    private static List<TEnum> ParseEnumValues<TEnum>(string[]? values)
        where TEnum : struct, Enum
    {
        var result = new List<TEnum>();
        if (values is null)
        {
            return result;
        }

        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (Enum.TryParse<TEnum>(value.Trim(), true, out var parsed))
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    private static bool ContainsNormalized(List<string> values, string expected)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] == expected)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<AiTemplateRecommendationResult?> RecommendAsync(
        OutreachProject project,
        AiClassificationRequest classification,
        AiQualificationResult qualification,
        CancellationToken cancellationToken)
    {
        if (project.Templates.Count == 0)
        {
            return null;
        }

        return await _ai.RecommendTemplateAsync(
            new AiTemplateRecommendationRequest
            {
                Industry = qualification.Industry ?? classification.Industry,
                CompanySize = qualification.CompanySize ?? classification.CompanySize,
                CompanyName = classification.CompanyName,
                ContactRole = qualification.ContactRole ?? classification.ContactRole,
                Country = classification.Country,
                Templates = project.Templates.Select(x => new AiTemplateOption
                {
                    TemplateId = x.TemplateId,
                    Name = x.Template?.Name ?? x.TemplateId.ToString(),
                    Category = x.Template?.Category,
                    MatchIndustry = x.MatchIndustry,
                    MatchCompanySize = x.MatchCompanySize,
                    MatchNotes = x.MatchNotes
                }).ToList()
            },
            cancellationToken);
    }

    private static AiClassificationRequest BuildClassification(
        OutreachProject project,
        CreateProspectDto request,
        string? email,
        string? whatsApp)
    {
        return new AiClassificationRequest
        {
            ProjectName = project.Name,
            ProjectDescription = project.Description,
            ResearchInstructions = project.ResearchInstructions,
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Countries = project.Countries.Select(x => x.Name).ToList(),
            Industries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            TargetRoles = project.TargetRoles.Select(x => x.Role).ToList(),
            CompanyName = request.CompanyName.Trim(),
            Website = TrimOrNull(request.Website),
            Country = TrimOrNull(request.Country),
            City = TrimOrNull(request.City),
            Industry = TrimOrNull(request.Industry),
            CompanyDescription = TrimOrNull(request.CompanyDescription),
            CompanySize = TrimOrNull(request.CompanySize),
            ContactName = TrimOrNull(request.ContactName),
            ContactRole = TrimOrNull(request.ContactRole),
            Email = email,
            WhatsApp = whatsApp,
            SourceUrl = TrimOrNull(request.SourceUrl),
            ResearchNotes = TrimOrNull(request.ResearchNotes)
        };
    }

    private static ProspectStatus ResolveManualStatus(
        AiQualificationResult qualification,
        int minScore,
        ProspectDuplicateMatch match,
        DuplicateHandlingMode duplicateHandling)
    {
        if (match.Kind == ProspectDuplicateKind.DoNotContact)
        {
            return ProspectStatus.DoNotContact;
        }

        if (match.NeedsReview
            || (match.Kind != ProspectDuplicateKind.None && duplicateHandling == DuplicateHandlingMode.Warn))
        {
            return ProspectStatus.NeedsReview;
        }

        if (qualification.IsQualified && qualification.QualificationScore >= minScore)
        {
            return ProspectStatus.ReadyForOutreach;
        }

        if (qualification.NeedsReview || qualification.IsRelevant)
        {
            return ProspectStatus.NeedsReview;
        }

        return ProspectStatus.Discovered;
    }

    private static ProspectSummaryDto MapSummary(Prospect prospect, string? projectName, string? templateName)
    {
        return new ProspectSummaryDto
        {
            Id = prospect.Id,
            ProjectId = prospect.ProjectId,
            ProjectName = projectName,
            ResearchRunId = prospect.ResearchRunId,
            CompanyName = prospect.CompanyName,
            Website = prospect.Website,
            Country = prospect.Country,
            City = prospect.City,
            Industry = prospect.Industry,
            CompanySize = prospect.CompanySize,
            ContactName = prospect.ContactName,
            ContactRole = prospect.ContactRole,
            Email = prospect.Email,
            Phone = prospect.Phone,
            WhatsApp = prospect.WhatsApp,
            SourceUrl = prospect.SourceUrl,
            SourceUrls = ProspectSourceUrlStore.Parse(prospect.SourceUrlsJson),
            SourceType = prospect.SourceType,
            SourceName = prospect.SourceName,
            QualificationScore = prospect.QualificationScore,
            EmailConfidenceScore = prospect.EmailConfidenceScore,
            WhatsAppConfidenceScore = prospect.WhatsAppConfidenceScore,
            IsQualified = prospect.IsQualified,
            Status = prospect.Status,
            DuplicateKind = prospect.DuplicateKind,
            RecommendedTemplateId = prospect.RecommendedTemplateId,
            RecommendedTemplateName = templateName,
            RecommendedTemplateReason = prospect.RecommendedTemplateReason,
            RecommendedTemplateConfidence = prospect.RecommendedTemplateConfidence,
            OutreachCampaignId = prospect.OutreachCampaignId,
            CreatedAtUtc = prospect.CreatedAtUtc,
            UpdatedAtUtc = prospect.UpdatedAtUtc
        };
    }

    private static ProspectDetailDto MapDetail(Prospect prospect, string? projectName, string? templateName)
    {
        var summary = MapSummary(prospect, projectName, templateName);
        return new ProspectDetailDto
        {
            Id = summary.Id,
            ProjectId = summary.ProjectId,
            ProjectName = summary.ProjectName,
            ResearchRunId = summary.ResearchRunId,
            CompanyName = summary.CompanyName,
            Website = summary.Website,
            Country = summary.Country,
            City = summary.City,
            Industry = summary.Industry,
            CompanySize = summary.CompanySize,
            ContactName = summary.ContactName,
            ContactRole = summary.ContactRole,
            Email = summary.Email,
            Phone = summary.Phone,
            WhatsApp = summary.WhatsApp,
            SourceUrl = summary.SourceUrl,
            SourceUrls = summary.SourceUrls,
            SourceType = summary.SourceType,
            SourceName = summary.SourceName,
            QualificationScore = summary.QualificationScore,
            EmailConfidenceScore = summary.EmailConfidenceScore,
            WhatsAppConfidenceScore = summary.WhatsAppConfidenceScore,
            IsQualified = summary.IsQualified,
            Status = summary.Status,
            DuplicateKind = summary.DuplicateKind,
            RecommendedTemplateId = summary.RecommendedTemplateId,
            RecommendedTemplateName = summary.RecommendedTemplateName,
            RecommendedTemplateReason = summary.RecommendedTemplateReason,
            RecommendedTemplateConfidence = summary.RecommendedTemplateConfidence,
            OutreachCampaignId = summary.OutreachCampaignId,
            CreatedAtUtc = summary.CreatedAtUtc,
            UpdatedAtUtc = summary.UpdatedAtUtc,
            CompanyDescription = prospect.CompanyDescription,
            LinkedInUrl = prospect.LinkedInUrl,
            AdditionalEmails = ProspectContactStore.Parse(prospect.AdditionalEmailsJson),
            AdditionalPhones = ProspectContactStore.Parse(prospect.AdditionalPhonesJson),
            ResearchNotes = prospect.ResearchNotes,
            QualificationSummary = prospect.QualificationSummary
        };
    }

    private static OutreachContactHistoryDto MapHistory(OutreachContactHistory item)
    {
        return new OutreachContactHistoryDto
        {
            Id = item.Id,
            Channel = item.Channel,
            ContactValue = item.ContactValue,
            CampaignId = item.CampaignId,
            TemplateId = item.TemplateId,
            ContactedAtUtc = item.ContactedAtUtc
        };
    }

    private static string? NormalizeEmailOrNull(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var parsed = EmailAddressParser.Parse(email);
        return parsed.ValidEmails.Count == 0 ? null : parsed.ValidEmails[0];
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
