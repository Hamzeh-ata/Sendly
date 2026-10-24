using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public class ContactAttributionResult
{
    public string? Email { get; set; }

    public string? EvidenceUrl { get; set; }

    public string? PageDomain { get; set; }

    public ContactAttributionState State { get; set; } = ContactAttributionState.Unverified;

    public int Confidence { get; set; }

    public string ReasonCode { get; set; } = "contact.unverified";

    public string Explanation { get; set; } = "Contact attribution is unverified.";
}

public static class ContactAttributionHelper
{
    private static readonly HashSet<string> GenericMailHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "yahoo.com",
        "icloud.com", "proton.me", "protonmail.com", "live.com", "aol.com"
    };

    public static ContactAttributionResult AttributeBest(
        IEnumerable<string>? emails,
        string? evidenceUrl,
        string? candidateWebsite,
        bool pageIsDirectory)
    {
        ContactAttributionResult? best = null;
        foreach (var email in emails ?? [])
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            var attributed = Attribute(email, evidenceUrl, candidateWebsite, pageIsDirectory);
            if (best is null || attributed.Confidence > best.Confidence)
            {
                best = attributed;
            }

            if (attributed.State == ContactAttributionState.SameDomain)
            {
                return attributed;
            }
        }

        return best ?? Attribute(null, evidenceUrl, candidateWebsite, pageIsDirectory);
    }

    public static ContactAttributionResult Attribute(
        string? email,
        string? evidenceUrl,
        string? candidateWebsite,
        bool pageIsDirectory)
    {
        var result = new ContactAttributionResult
        {
            Email = email,
            EvidenceUrl = evidenceUrl,
            PageDomain = UrlCanonicalizer.ExtractDomain(evidenceUrl)
        };

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            result.State = ContactAttributionState.Rejected;
            result.ReasonCode = "contact.missing";
            result.Explanation = "No email address was provided.";
            return result;
        }

        var emailHost = email.Split('@').LastOrDefault()?.Trim().ToLowerInvariant() ?? string.Empty;
        var websiteHost = UrlCanonicalizer.ExtractDomain(candidateWebsite) ?? string.Empty;
        var pageHost = result.PageDomain ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(websiteHost)
            && HostsMatch(emailHost, websiteHost))
        {
            result.State = ContactAttributionState.SameDomain;
            result.Confidence = pageIsDirectory ? 85 : 90;
            result.ReasonCode = pageIsDirectory
                ? "contact.directory_official_domain"
                : "contact.same_domain";
            result.Explanation = pageIsDirectory
                ? "Email domain matches the organization's official website even though evidence was found on a directory listing."
                : "Email domain matches the candidate website domain.";
            return result;
        }

        if (pageIsDirectory)
        {
            result.State = ContactAttributionState.DirectoryListing;
            result.Confidence = 35;
            result.ReasonCode = "contact.directory_listing";
            result.Explanation =
                "Email was found on a directory/listing page and is not auto-attributed to the page operator.";
            return result;
        }

        if (GenericMailHosts.Contains(emailHost))
        {
            result.State = ContactAttributionState.Unverified;
            result.Confidence = 40;
            result.ReasonCode = "contact.generic_mailbox";
            result.Explanation = "Email uses a generic mailbox provider; attribution remains unverified.";
            return result;
        }

        if (!string.IsNullOrWhiteSpace(pageHost)
            && !string.IsNullOrWhiteSpace(websiteHost)
            && !HostsMatch(pageHost, websiteHost))
        {
            result.State = ContactAttributionState.Rejected;
            result.Confidence = 15;
            result.ReasonCode = HostsMatch(emailHost, pageHost)
                ? "contact.page_operator_mismatch"
                : "contact.third_party_page";
            result.Explanation =
                "Email was observed on a third-party page and is not attributed as an official business contact.";
            return result;
        }

        if (!string.IsNullOrWhiteSpace(websiteHost)
            && !HostsMatch(emailHost, websiteHost)
            && !HostsMatch(emailHost, pageHost))
        {
            result.State = ContactAttributionState.Rejected;
            result.Confidence = 20;
            result.ReasonCode = "contact.unrelated_mailbox";
            result.Explanation =
                "Email domain does not match the candidate website or evidence page domain.";
            return result;
        }

        result.State = ContactAttributionState.CrossDomainEvidence;
        result.Confidence = 35;
        result.ReasonCode = "contact.cross_domain";
        result.Explanation =
            "Email domain differs from the candidate website; retained as unattributed evidence only.";
        return result;
    }

    private static bool HostsMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var a = NormalizeHost(left);
        var b = NormalizeHost(right);
        return a == b || a.EndsWith("." + b, StringComparison.Ordinal) || b.EndsWith("." + a, StringComparison.Ordinal);
    }

    private static string NormalizeHost(string host)
    {
        var value = host.Trim().ToLowerInvariant();
        if (value.StartsWith("www.", StringComparison.Ordinal))
        {
            value = value[4..];
        }

        return value;
    }
}
