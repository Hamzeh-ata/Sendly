using Sendly.Application.Abstractions;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public enum OrganizationCountryMatchState
{
    Unknown = 0,
    Matched = 1,
    Mismatch = 2,
    NeedsReview = 3
}

public class OrganizationCountryGateResult
{
    public OrganizationCountryMatchState State { get; set; } = OrganizationCountryMatchState.Unknown;

    public bool Passed => State == OrganizationCountryMatchState.Matched;

    public bool IsMandatoryFailure => State == OrganizationCountryMatchState.Mismatch;

    public string? InferredCountry { get; set; }

    public string ReasonCode { get; set; } = "country.unknown";

    public string Explanation { get; set; } = "No country evidence was available.";

    public List<string> Evidence { get; set; } = [];
}

public static class OrganizationCountryGate
{
    public static OrganizationCountryGateResult Evaluate(
        ProspectCandidate candidate,
        IReadOnlyList<string> targetCountries)
    {
        if (targetCountries.Count == 0)
        {
            return new OrganizationCountryGateResult
            {
                State = OrganizationCountryMatchState.Matched,
                InferredCountry = NullIfBlank(candidate.Country),
                ReasonCode = "country.not_required",
                Explanation = "No mandatory target country was configured."
            };
        }

        var evidence = CollectEvidence(candidate);
        var conflicting = FindConflictingEvidence(evidence, targetCountries);
        if (conflicting.Count > 0)
        {
            return new OrganizationCountryGateResult
            {
                State = OrganizationCountryMatchState.Mismatch,
                InferredCountry = conflicting[0].Country,
                ReasonCode = "country.mismatch_evidence",
                Explanation =
                    $"Organization location evidence indicates '{conflicting[0].Country}', which is outside the target country scope.",
                Evidence = conflicting.Select(x => x.Summary).ToList()
            };
        }

        var matching = FindMatchingEvidence(evidence, targetCountries);
        if (matching.Count > 0)
        {
            return new OrganizationCountryGateResult
            {
                State = OrganizationCountryMatchState.Matched,
                InferredCountry = matching[0].Country,
                ReasonCode = "country.matched_evidence",
                Explanation =
                    $"Organization location evidence matched target country '{matching[0].Country}'.",
                Evidence = matching.Select(x => x.Summary).ToList()
            };
        }

        if (!string.IsNullOrWhiteSpace(candidate.Country)
            && CountryScopeHelper.CountryMatches(candidate.Country, targetCountries)
            && evidence.Count == 0)
        {
            return new OrganizationCountryGateResult
            {
                State = OrganizationCountryMatchState.NeedsReview,
                InferredCountry = candidate.Country,
                ReasonCode = "country.stamped_without_evidence",
                Explanation =
                    "Candidate country was present without independent geographic evidence; mandatory country cannot be confirmed.",
                Evidence = []
            };
        }

        return new OrganizationCountryGateResult
        {
            State = OrganizationCountryMatchState.NeedsReview,
            InferredCountry = NullIfBlank(candidate.Country),
            ReasonCode = "country.evidence_missing",
            Explanation =
                "Mandatory target country could not be verified from address, path, host, or page location evidence.",
            Evidence = []
        };
    }

    public static string? InferCountryFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var fromHost = CountryScopeHelper.InferCountryFromHost(uri.Host);
        if (!string.IsNullOrWhiteSpace(fromHost))
        {
            return fromHost;
        }

        return CountryScopeHelper.InferCountryFromPath(uri.AbsolutePath);
    }

    public static string? ResolveObservedCountry(ProspectCandidate candidate)
    {
        var evidence = CollectEvidence(candidate);
        if (evidence.Count > 0)
        {
            return evidence[0].Country;
        }

        return NullIfBlank(candidate.Country);
    }

    private static List<CountryEvidenceItem> CollectEvidence(ProspectCandidate candidate)
    {
        var items = new List<CountryEvidenceItem>();
        AddUrlEvidence(items, candidate.Website, "official_website");
        AddUrlEvidence(items, candidate.SourceUrl, "source_url");
        foreach (var extra in candidate.AdditionalSourceUrls)
        {
            AddUrlEvidence(items, extra, "additional_source");
        }

        if (!string.IsNullOrWhiteSpace(candidate.CountryEvidenceSummary)
            && !string.IsNullOrWhiteSpace(candidate.Country))
        {
            items.Add(new CountryEvidenceItem(
                candidate.Country!,
                $"candidate_country:{candidate.CountryEvidenceSummary}"));
        }
        else if (!string.IsNullOrWhiteSpace(candidate.Country)
                 && !string.IsNullOrWhiteSpace(candidate.CountryEvidenceSource)
                 && !string.Equals(candidate.CountryEvidenceSource, "query_target_stamp", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new CountryEvidenceItem(
                candidate.Country!,
                $"candidate_country_source:{candidate.CountryEvidenceSource}"));
        }

        var text = $"{candidate.CompanyName} {candidate.CompanyDescription} {candidate.ResearchNotes} {candidate.City}";
        foreach (var known in KnownCountries)
        {
            if (CountryScopeHelper.TextMentionsCountry(text, [known]))
            {
                items.Add(new CountryEvidenceItem(known, $"text_mention:{known}"));
            }
        }

        return Deduplicate(items);
    }

    private static void AddUrlEvidence(List<CountryEvidenceItem> items, string? url, string label)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        var fromPath = CountryScopeHelper.InferCountryFromPath(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(fromPath))
        {
            items.Add(new CountryEvidenceItem(fromPath, $"{label}_path:{uri.AbsolutePath}"));
        }

        var fromHost = CountryScopeHelper.InferCountryFromHost(uri.Host);
        if (!string.IsNullOrWhiteSpace(fromHost))
        {
            items.Add(new CountryEvidenceItem(fromHost, $"{label}_host:{uri.Host}"));
        }
    }

    private static List<CountryEvidenceItem> FindMatchingEvidence(
        List<CountryEvidenceItem> evidence,
        IReadOnlyList<string> targets)
    {
        var matched = new List<CountryEvidenceItem>();
        foreach (var item in evidence)
        {
            if (CountryScopeHelper.CountryMatches(item.Country, targets))
            {
                matched.Add(item);
            }
        }

        return matched;
    }

    private static List<CountryEvidenceItem> FindConflictingEvidence(
        List<CountryEvidenceItem> evidence,
        IReadOnlyList<string> targets)
    {
        var conflicts = new List<CountryEvidenceItem>();
        foreach (var item in evidence)
        {
            if (!CountryScopeHelper.CountryMatches(item.Country, targets))
            {
                conflicts.Add(item);
            }
        }

        return conflicts;
    }

    private static List<CountryEvidenceItem> Deduplicate(List<CountryEvidenceItem> items)
    {
        var result = new List<CountryEvidenceItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var key = CountryScopeHelper.NormalizeCountryKey(item.Country) + "|" + item.Summary;
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly string[] KnownCountries =
    [
        "United States", "United Kingdom", "Saudi Arabia", "United Arab Emirates",
        "Vietnam", "Singapore", "Malaysia", "Switzerland", "Indonesia", "Japan",
        "Canada", "Australia", "New Zealand", "Egypt", "Jordan", "Germany", "France",
        "India", "China", "South Korea", "Thailand", "Philippines"
    ];

    private readonly record struct CountryEvidenceItem(string Country, string Summary);
}
