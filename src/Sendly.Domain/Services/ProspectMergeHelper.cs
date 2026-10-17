using Sendly.Domain.Entities;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Services;

public static class ProspectMergeHelper
{
    public static void MergeCandidateInto(
        Prospect target,
        ProspectMergeInput input)
    {
        target.CompanyName = PreferLongerDisplayName(target.CompanyName, input.CompanyName);
        target.NormalizedCompanyName = ProspectNormalizer.NormalizeCompanyName(target.CompanyName);
        target.Website = PreferFilled(target.Website, input.Website);
        target.NormalizedWebsite = ProspectNormalizer.NormalizeWebsite(target.Website)
            ?? ProspectNormalizer.NormalizeWebsite(input.Website)
            ?? target.NormalizedWebsite;
        target.Country = PreferFilled(target.Country, input.Country);
        target.City = PreferFilled(target.City, input.City);
        target.NormalizedCity = ProspectNormalizer.NormalizeCity(target.City) ?? target.NormalizedCity;
        target.Industry = PreferFilled(target.Industry, input.Industry);
        target.CompanyDescription = PreferLonger(target.CompanyDescription, input.CompanyDescription);
        target.CompanySize = PreferFilled(target.CompanySize, input.CompanySize);
        target.ContactName = PreferFilled(target.ContactName, input.ContactName);
        target.ContactRole = PreferFilled(target.ContactRole, input.ContactRole);
        target.LinkedInUrl = PreferFilled(target.LinkedInUrl, input.LinkedInUrl);

        MergeEmail(target, input.Email);
        MergePhone(target, input.Phone);
        MergeWhatsApp(target, input.WhatsApp);

        target.SourceUrlsJson = ProspectSourceUrlStore.Merge(
            target.SourceUrlsJson,
            [target.SourceUrl, input.SourceUrl, ..input.AdditionalSourceUrls]);

        if (string.IsNullOrWhiteSpace(target.SourceUrl))
        {
            target.SourceUrl = PreferFilled(target.SourceUrl, input.SourceUrl);
        }

        if (string.IsNullOrWhiteSpace(target.SourceName))
        {
            target.SourceName = PreferFilled(target.SourceName, input.SourceName);
        }
        else if (!string.IsNullOrWhiteSpace(input.SourceName)
                 && !ContainsIgnoreCase(target.SourceName, input.SourceName))
        {
            target.SourceName = $"{target.SourceName} + {input.SourceName}";
            if (target.SourceName.Length > 200)
            {
                target.SourceName = target.SourceName[..200];
            }
        }

        if (target.SourceType == ProspectSourceType.Manual && input.SourceType != ProspectSourceType.Manual)
        {
            target.SourceType = input.SourceType;
        }

        target.ResearchNotes = MergeNotes(target.ResearchNotes, input.ResearchNotes);
        target.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static void ApplyQualificationMerge(
        Prospect target,
        string? qualificationSummary,
        int? qualificationScore,
        int? emailConfidence,
        int? whatsAppConfidence,
        ProspectStatus status,
        bool isQualified,
        ProspectDuplicateKind duplicateKind,
        Guid? researchRunId,
        Guid? recommendedTemplateId,
        string? recommendedTemplateReason,
        int? recommendedTemplateConfidence)
    {
        if (!string.IsNullOrWhiteSpace(qualificationSummary))
        {
            target.QualificationSummary = PreferLonger(target.QualificationSummary, qualificationSummary);
        }

        if (qualificationScore is not null
            && (target.QualificationScore is null || qualificationScore.Value >= target.QualificationScore.Value))
        {
            target.QualificationScore = qualificationScore;
        }

        if (emailConfidence is not null
            && (target.EmailConfidenceScore is null || emailConfidence.Value >= target.EmailConfidenceScore.Value))
        {
            target.EmailConfidenceScore = emailConfidence;
        }

        if (whatsAppConfidence is not null
            && (target.WhatsAppConfidenceScore is null || whatsAppConfidence.Value >= target.WhatsAppConfidenceScore.Value))
        {
            target.WhatsAppConfidenceScore = whatsAppConfidence;
        }

        if (ShouldUpgradeStatus(target.Status, status))
        {
            target.Status = status;
        }

        target.IsQualified = target.IsQualified || isQualified;
        if (duplicateKind != ProspectDuplicateKind.None)
        {
            target.DuplicateKind = duplicateKind;
        }

        if (researchRunId is not null)
        {
            target.ResearchRunId = researchRunId;
        }

        if (recommendedTemplateId is not null && target.RecommendedTemplateId is null)
        {
            target.RecommendedTemplateId = recommendedTemplateId;
            target.RecommendedTemplateReason = recommendedTemplateReason;
            target.RecommendedTemplateConfidence = recommendedTemplateConfidence;
        }

        target.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void MergeEmail(Prospect target, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var normalized = EmailAddressParser.Normalize(email);
        if (string.IsNullOrWhiteSpace(target.Email))
        {
            target.Email = email.Trim();
            target.NormalizedEmail = normalized;
            return;
        }

        var existingNormalized = target.NormalizedEmail ?? EmailAddressParser.Normalize(target.Email);
        if (string.Equals(existingNormalized, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        target.AdditionalEmailsJson = ProspectContactStore.MergeEmails(
            target.AdditionalEmailsJson,
            null,
            [email]);
    }

    private static void MergePhone(Prospect target, string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        var normalized = ProspectNormalizer.NormalizePhone(phone);
        if (normalized is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(target.Phone))
        {
            target.Phone = phone.Trim();
            target.NormalizedPhone = normalized;
            return;
        }

        var existingNormalized = target.NormalizedPhone ?? ProspectNormalizer.NormalizePhone(target.Phone);
        if (string.Equals(existingNormalized, normalized, StringComparison.Ordinal))
        {
            return;
        }

        target.AdditionalPhonesJson = ProspectContactStore.MergePhones(
            target.AdditionalPhonesJson,
            null,
            [phone]);
    }

    private static void MergeWhatsApp(Prospect target, string? whatsApp)
    {
        if (string.IsNullOrWhiteSpace(whatsApp))
        {
            return;
        }

        var normalized = ProspectNormalizer.NormalizeWhatsApp(whatsApp);
        if (normalized is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(target.WhatsApp))
        {
            target.WhatsApp = whatsApp.Trim();
            target.NormalizedWhatsApp = normalized;
            return;
        }

        var existingNormalized = target.NormalizedWhatsApp ?? ProspectNormalizer.NormalizeWhatsApp(target.WhatsApp);
        if (string.Equals(existingNormalized, normalized, StringComparison.Ordinal))
        {
            return;
        }

        target.AdditionalPhonesJson = ProspectContactStore.MergePhones(
            target.AdditionalPhonesJson,
            null,
            [whatsApp]);
    }

    private static bool ShouldUpgradeStatus(ProspectStatus current, ProspectStatus incoming)
    {
        if (current == ProspectStatus.DoNotContact)
        {
            return false;
        }

        if (incoming == ProspectStatus.DoNotContact)
        {
            return true;
        }

        return StatusRank(incoming) > StatusRank(current);
    }

    private static int StatusRank(ProspectStatus status)
    {
        return status switch
        {
            ProspectStatus.Rejected => 0,
            ProspectStatus.Discovered => 1,
            ProspectStatus.Researching => 2,
            ProspectStatus.NeedsReview => 3,
            ProspectStatus.Qualified => 4,
            ProspectStatus.ReadyForOutreach => 5,
            ProspectStatus.Contacted => 6,
            ProspectStatus.DoNotContact => 7,
            _ => 0
        };
    }

    private static string PreferLongerDisplayName(string existing, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return existing;
        }

        var trimmed = incoming.Trim();
        if (string.IsNullOrWhiteSpace(existing))
        {
            return trimmed;
        }

        return trimmed.Length > existing.Length ? trimmed : existing;
    }

    private static string? PreferFilled(string? existing, string? incoming)
    {
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing.Trim();
        }

        return string.IsNullOrWhiteSpace(incoming) ? null : incoming.Trim();
    }

    private static string? PreferLonger(string? existing, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return incoming.Trim();
        }

        return incoming.Trim().Length > existing.Length ? incoming.Trim() : existing;
    }

    private static string? MergeNotes(string? existing, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return incoming.Trim();
        }

        if (ContainsIgnoreCase(existing, incoming))
        {
            return existing;
        }

        var merged = $"{existing.Trim()} | {incoming.Trim()}";
        return merged.Length <= 4000 ? merged : merged[..4000];
    }

    private static bool ContainsIgnoreCase(string haystack, string needle)
    {
        return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}

public class ProspectMergeInput
{
    public string CompanyName { get; set; } = string.Empty;

    public string? Website { get; set; }

    public string? Country { get; set; }

    public string? City { get; set; }

    public string? Industry { get; set; }

    public string? CompanyDescription { get; set; }

    public string? CompanySize { get; set; }

    public string? ContactName { get; set; }

    public string? ContactRole { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? WhatsApp { get; set; }

    public string? LinkedInUrl { get; set; }

    public string? SourceUrl { get; set; }

    public List<string> AdditionalSourceUrls { get; set; } = [];

    public ProspectSourceType SourceType { get; set; }

    public string? SourceName { get; set; }

    public string? ResearchNotes { get; set; }
}
