using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public class OfficialDomainRecoveryResult
{
    public List<DiscoveredUrlCandidate> Candidates { get; set; } = [];

    public int InputLinks { get; set; }

    public int Rejected { get; set; }

    public int Duplicates { get; set; }

    public string OutcomeReason { get; set; } = "recovery.none";
}

public static class OfficialDomainRecovery
{
    public static OfficialDomainRecoveryResult BuildCandidates(
        IEnumerable<string> externalLinks,
        string? parentUrl,
        IReadOnlyCollection<string> knownDomains,
        IReadOnlyCollection<string> knownCanonicalUrls,
        IReadOnlyList<string> industries,
        IReadOnlyList<string> countries,
        int maxCandidates)
    {
        var result = new OfficialDomainRecoveryResult();
        var knownDomainSet = new HashSet<string>(
            knownDomains.Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);
        var knownUrlSet = new HashSet<string>(
            knownCanonicalUrls.Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limit = Math.Clamp(maxCandidates, 1, 50);
        var parentDomain = UrlCanonicalizer.ExtractDomain(parentUrl);

        foreach (var link in externalLinks)
        {
            result.InputLinks++;
            var admission = UrlAdmissionPolicy.Evaluate(link);
            if (!admission.Accepted || string.IsNullOrWhiteSpace(admission.NormalizedUrl))
            {
                result.Rejected++;
                continue;
            }

            if (admission.PagePurpose is UrlPagePurpose.DirectoryListing
                or UrlPagePurpose.AssetOrApi
                or UrlPagePurpose.JobListing
                or UrlPagePurpose.Navigation
                or UrlPagePurpose.Listicle)
            {
                result.Rejected++;
                continue;
            }

            var domain = admission.NormalizedDomain ?? UrlCanonicalizer.ExtractDomain(admission.NormalizedUrl);
            if (string.IsNullOrWhiteSpace(domain))
            {
                result.Rejected++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(parentDomain)
                && string.Equals(domain, parentDomain, StringComparison.OrdinalIgnoreCase))
            {
                result.Rejected++;
                continue;
            }

            if (SystemDirectoryCatalog.IsSeedOrDirectoryHost(domain))
            {
                result.Rejected++;
                continue;
            }

            if (knownDomainSet.Contains(domain))
            {
                result.Duplicates++;
                continue;
            }

            if (!seen.Add(domain) || !knownUrlSet.Add(admission.NormalizedUrl))
            {
                result.Duplicates++;
                continue;
            }

            var score = UrlPriorityScorer.Score(
                admission.NormalizedUrl,
                DiscoverySourceType.LinkExpansion,
                industries,
                countries,
                parentDomain,
                depth: 1,
                alreadyCrawled: false,
                pagePurpose: admission.PagePurpose);
            var purposeUseful = admission.PagePurpose is UrlPagePurpose.CompanyProfile
                or UrlPagePurpose.Contact
                or UrlPagePurpose.About
                or UrlPagePurpose.Team
                or UrlPagePurpose.Location;
            if (!purposeUseful && !score.IsDiscoveryUseful && score.Total < 24)
            {
                result.Rejected++;
                continue;
            }

            var root = "https://" + domain;
            result.Candidates.Add(new DiscoveredUrlCandidate
            {
                Url = root,
                SourceType = DiscoverySourceType.LinkExpansion,
                SourceName = "Official domain recovery",
                Depth = 0
            });

            if (admission.PagePurpose is UrlPagePurpose.Contact or UrlPagePurpose.About or UrlPagePurpose.Team)
            {
                result.Candidates.Add(new DiscoveredUrlCandidate
                {
                    Url = admission.NormalizedUrl,
                    SourceType = DiscoverySourceType.LinkExpansion,
                    SourceName = "Official domain recovery",
                    Depth = 1
                });
            }

            if (result.Candidates.Count >= limit)
            {
                break;
            }
        }

        result.OutcomeReason = result.Candidates.Count > 0
            ? "recovery.official_domains_added"
            : result.InputLinks == 0
                ? "recovery.no_external_links"
                : "recovery.all_links_rejected_or_duplicate";
        return result;
    }
}
