using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public static class ProspectCandidateDeduplicator
{
    public static List<ProspectCandidate> DeduplicateAndMerge(IEnumerable<ProspectCandidate> candidates)
    {
        var result = new List<ProspectCandidate>();
        foreach (var candidate in candidates)
        {
            var matchIndex = FindMatchIndex(result, candidate);
            if (matchIndex < 0)
            {
                result.Add(Clone(candidate));
                continue;
            }

            result[matchIndex] = Merge(result[matchIndex], candidate);
        }

        return result;
    }

    private static int FindMatchIndex(List<ProspectCandidate> existing, ProspectCandidate candidate)
    {
        var email = ProspectNormalizer.NormalizeEmail(candidate.Email);
        var phone = ProspectNormalizer.NormalizePhone(candidate.Phone);
        var whatsApp = ProspectNormalizer.NormalizeWhatsApp(candidate.WhatsApp);
        var website = ResolveComparableWebsite(candidate);
        var company = ProspectNormalizer.NormalizeCompanyName(candidate.CompanyName);
        var city = ProspectNormalizer.NormalizeCity(candidate.City);
        var sourceUrl = ProspectNormalizer.NormalizeUrl(candidate.SourceUrl);

        for (var i = 0; i < existing.Count; i++)
        {
            var current = existing[i];
            var currentEmail = ProspectNormalizer.NormalizeEmail(current.Email);
            var currentPhone = ProspectNormalizer.NormalizePhone(current.Phone);
            var currentWhatsApp = ProspectNormalizer.NormalizeWhatsApp(current.WhatsApp);
            var currentWebsite = ResolveComparableWebsite(current);
            var currentCompany = ProspectNormalizer.NormalizeCompanyName(current.CompanyName);
            var currentCity = ProspectNormalizer.NormalizeCity(current.City);
            var currentSourceUrl = ProspectNormalizer.NormalizeUrl(current.SourceUrl);

            if (!string.IsNullOrWhiteSpace(email)
                && (string.Equals(email, currentEmail, StringComparison.OrdinalIgnoreCase)
                    || ContainsNormalized(current.AdditionalEmails, email, ProspectNormalizer.NormalizeEmail)))
            {
                return i;
            }

            if (EmailsOverlap(candidate, current))
            {
                return i;
            }

            if (!string.IsNullOrWhiteSpace(whatsApp)
                && (string.Equals(whatsApp, currentWhatsApp, StringComparison.Ordinal)
                    || ContainsNormalized(current.AdditionalPhones, whatsApp, ProspectNormalizer.NormalizePhone)))
            {
                return i;
            }

            if (!string.IsNullOrWhiteSpace(phone)
                && (string.Equals(phone, currentPhone, StringComparison.Ordinal)
                    || string.Equals(phone, currentWhatsApp, StringComparison.Ordinal)
                    || ContainsNormalized(current.AdditionalPhones, phone, ProspectNormalizer.NormalizePhone)))
            {
                return i;
            }

            if (!string.IsNullOrWhiteSpace(website)
                && string.Equals(website, currentWebsite, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }

            if (!string.IsNullOrWhiteSpace(sourceUrl)
                && (string.Equals(sourceUrl, currentSourceUrl, StringComparison.OrdinalIgnoreCase)
                    || ContainsNormalizedUrl(current.AdditionalSourceUrls, sourceUrl)))
            {
                return i;
            }

            if (!string.IsNullOrWhiteSpace(company)
                && ProspectNormalizer.AreCompanyNamesLikelySame(company, currentCompany)
                && !string.IsNullOrWhiteSpace(city)
                && !string.IsNullOrWhiteSpace(currentCity)
                && string.Equals(city, currentCity, StringComparison.OrdinalIgnoreCase)
                && HasStrongSupportingEvidence(candidate, current))
            {
                return i;
            }
        }

        return -1;
    }

    private static ProspectCandidate Merge(ProspectCandidate target, ProspectCandidate incoming)
    {
        target.CompanyName = PreferLonger(target.CompanyName, incoming.CompanyName) ?? target.CompanyName;
        target.Website = PreferFilled(target.Website, incoming.Website);
        target.Country = PreferFilled(target.Country, incoming.Country);
        target.City = PreferFilled(target.City, incoming.City);
        target.Industry = PreferFilled(target.Industry, incoming.Industry);
        target.CompanyDescription = PreferLonger(target.CompanyDescription, incoming.CompanyDescription);
        target.CompanySize = PreferFilled(target.CompanySize, incoming.CompanySize);
        target.ContactName = PreferFilled(target.ContactName, incoming.ContactName);
        target.ContactRole = PreferFilled(target.ContactRole, incoming.ContactRole);
        MergeEmailFields(target, incoming);
        MergePhoneFields(target, incoming);
        target.LinkedInUrl = PreferFilled(target.LinkedInUrl, incoming.LinkedInUrl);
        MergeSourceFields(target, incoming);

        if (target.SourceType == ProspectSourceType.Other && incoming.SourceType != ProspectSourceType.Other)
        {
            target.SourceType = incoming.SourceType;
        }

        target.ResearchNotes = MergeNotes(target.ResearchNotes, incoming.ResearchNotes);
        return target;
    }

    private static string? ResolveComparableWebsite(ProspectCandidate candidate)
    {
        var website = ProspectNormalizer.NormalizeWebsite(candidate.Website);
        if (!string.IsNullOrWhiteSpace(website)
            && !ResearchDirectoryCatalog.IsDirectoryHost(website))
        {
            return website;
        }

        if (string.IsNullOrWhiteSpace(candidate.SourceUrl)
            || !Uri.TryCreate(candidate.SourceUrl, UriKind.Absolute, out var sourceUri))
        {
            return null;
        }

        if (ResearchDirectoryCatalog.IsDirectoryUrl(sourceUri))
        {
            return null;
        }

        return ProspectNormalizer.NormalizeWebsite(candidate.SourceUrl);
    }

    private static void MergeEmailFields(ProspectCandidate target, ProspectCandidate incoming)
    {
        var emails = new List<string?>();
        emails.Add(target.Email);
        emails.AddRange(target.AdditionalEmails);
        emails.Add(incoming.Email);
        emails.AddRange(incoming.AdditionalEmails);

        var unique = DeduplicateByNormalize(emails, ProspectNormalizer.NormalizeEmail);
        target.Email = unique.Count > 0 ? unique[0] : null;
        target.AdditionalEmails = unique.Count > 1 ? unique.Skip(1).ToList() : [];
    }

    private static void MergePhoneFields(ProspectCandidate target, ProspectCandidate incoming)
    {
        var phones = new List<string?>();
        phones.Add(target.Phone);
        phones.Add(target.WhatsApp);
        phones.AddRange(target.AdditionalPhones);
        phones.Add(incoming.Phone);
        phones.Add(incoming.WhatsApp);
        phones.AddRange(incoming.AdditionalPhones);

        var unique = DeduplicateByNormalize(phones, ProspectNormalizer.NormalizePhone);
        target.Phone = unique.Count > 0 ? unique[0] : null;
        target.WhatsApp = PreferFilled(target.WhatsApp, incoming.WhatsApp) ?? target.Phone;
        if (!string.IsNullOrWhiteSpace(target.WhatsApp))
        {
            var whatsAppKey = ProspectNormalizer.NormalizeWhatsApp(target.WhatsApp);
            unique = unique
                .Where(x => !string.Equals(
                    ProspectNormalizer.NormalizePhone(x),
                    whatsAppKey,
                    StringComparison.Ordinal))
                .ToList();
            if (string.Equals(
                    ProspectNormalizer.NormalizePhone(target.Phone),
                    whatsAppKey,
                    StringComparison.Ordinal)
                && unique.Count > 0)
            {
                target.Phone = unique[0];
                unique = unique.Skip(1).ToList();
            }
            else if (string.Equals(
                         ProspectNormalizer.NormalizePhone(target.Phone),
                         whatsAppKey,
                         StringComparison.Ordinal))
            {
                target.Phone = target.WhatsApp;
            }
        }

        target.AdditionalPhones = unique
            .Where(x => !string.Equals(
                ProspectNormalizer.NormalizePhone(x),
                ProspectNormalizer.NormalizePhone(target.Phone),
                StringComparison.Ordinal))
            .ToList();
    }

    private static void MergeSourceFields(ProspectCandidate target, ProspectCandidate incoming)
    {
        var urls = new List<string?>();
        urls.Add(target.SourceUrl);
        urls.AddRange(target.AdditionalSourceUrls);
        urls.Add(incoming.SourceUrl);
        urls.AddRange(incoming.AdditionalSourceUrls);
        var unique = ProspectSourceUrlStore.DeduplicateUrls(urls);
        target.SourceUrl = unique.Count > 0 ? unique[0] : null;
        target.AdditionalSourceUrls = unique.Count > 1 ? unique.Skip(1).ToList() : [];

        if (string.IsNullOrWhiteSpace(target.SourceName))
        {
            target.SourceName = incoming.SourceName;
        }
        else if (!string.IsNullOrWhiteSpace(incoming.SourceName)
                 && !target.SourceName.Contains(incoming.SourceName, StringComparison.OrdinalIgnoreCase))
        {
            target.SourceName = $"{target.SourceName} + {incoming.SourceName}";
        }
    }

    private static List<string> DeduplicateByNormalize(
        IEnumerable<string?> values,
        Func<string?, string?> normalize)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var key = normalize(value);
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
            {
                continue;
            }

            result.Add(value.Trim());
        }

        return result;
    }

    private static bool HasStrongSupportingEvidence(ProspectCandidate left, ProspectCandidate right)
    {
        var leftWebsite = ProspectNormalizer.NormalizeWebsite(left.Website)
            ?? ProspectNormalizer.NormalizeWebsite(left.SourceUrl);
        var rightWebsite = ProspectNormalizer.NormalizeWebsite(right.Website)
            ?? ProspectNormalizer.NormalizeWebsite(right.SourceUrl);
        if (!string.IsNullOrWhiteSpace(leftWebsite)
            && string.Equals(leftWebsite, rightWebsite, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftPhone = ProspectNormalizer.NormalizePhone(left.Phone)
            ?? ProspectNormalizer.NormalizeWhatsApp(left.WhatsApp);
        var rightPhone = ProspectNormalizer.NormalizePhone(right.Phone)
            ?? ProspectNormalizer.NormalizeWhatsApp(right.WhatsApp);
        if (!string.IsNullOrWhiteSpace(leftPhone)
            && string.Equals(leftPhone, rightPhone, StringComparison.Ordinal))
        {
            return true;
        }

        return EmailsOverlap(left, right);
    }

    private static bool EmailsOverlap(ProspectCandidate left, ProspectCandidate right)
    {
        var leftEmails = CollectEmails(left);
        var rightEmails = CollectEmails(right);
        foreach (var email in leftEmails)
        {
            if (rightEmails.Contains(email))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> CollectEmails(ProspectCandidate candidate)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primary = ProspectNormalizer.NormalizeEmail(candidate.Email);
        if (!string.IsNullOrWhiteSpace(primary))
        {
            set.Add(primary);
        }

        foreach (var email in candidate.AdditionalEmails)
        {
            var normalized = ProspectNormalizer.NormalizeEmail(email);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                set.Add(normalized);
            }
        }

        return set;
    }

    private static bool ContainsNormalized(
        IEnumerable<string> values,
        string normalized,
        Func<string?, string?> normalize)
    {
        foreach (var value in values)
        {
            if (string.Equals(normalize(value), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsNormalizedUrl(IEnumerable<string> values, string normalizedUrl)
    {
        foreach (var value in values)
        {
            var key = ProspectNormalizer.NormalizeUrl(value);
            if (string.Equals(key, normalizedUrl, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

        if (existing.Contains(incoming, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        var merged = $"{existing.Trim()} | {incoming.Trim()}";
        return merged.Length <= 4000 ? merged : merged[..4000];
    }

    private static ProspectCandidate Clone(ProspectCandidate source)
    {
        return new ProspectCandidate
        {
            CompanyName = source.CompanyName,
            Website = source.Website,
            Country = source.Country,
            City = source.City,
            Industry = source.Industry,
            CompanyDescription = source.CompanyDescription,
            CompanySize = source.CompanySize,
            ContactName = source.ContactName,
            ContactRole = source.ContactRole,
            Email = source.Email,
            AdditionalEmails = [..source.AdditionalEmails],
            Phone = source.Phone,
            AdditionalPhones = [..source.AdditionalPhones],
            WhatsApp = source.WhatsApp,
            LinkedInUrl = source.LinkedInUrl,
            SourceUrl = source.SourceUrl,
            AdditionalSourceUrls = [..source.AdditionalSourceUrls],
            SourceType = source.SourceType,
            SourceName = source.SourceName,
            ResearchNotes = source.ResearchNotes
        };
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
}
