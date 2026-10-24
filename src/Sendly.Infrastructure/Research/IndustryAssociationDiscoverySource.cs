using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Infrastructure.Research;

public class IndustryAssociationDiscoverySource : IUrlDiscoverySource
{
    public string Name => "IndustryAssociationCatalog";

    public bool IsEnabled(UrlDiscoveryContext context) => context.EnableDirectoryDiscovery;

    public Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var industries = MergeIndustries(context);
        var resolved = SystemDirectoryCatalog.ResolveWithDecisions(context.Countries, industries);
        context.LastSourceEligibility = resolved.Eligibility;
        foreach (var decision in resolved.Eligibility.Decisions)
        {
            if (!context.SourceEligibilityDecisions.Any(x =>
                    string.Equals(x.SourceName, decision.SourceName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.SourceUrl, decision.SourceUrl, StringComparison.OrdinalIgnoreCase)))
            {
                context.SourceEligibilityDecisions.Add(decision);
            }
        }

        var result = new List<DiscoveredUrlCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in resolved.Entries)
        {
            if (!string.Equals(entry.SourceKind, "Association", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var canonical = UrlCanonicalizer.Canonicalize(entry.BaseUrl);
            if (string.IsNullOrWhiteSpace(canonical) || !seen.Add(canonical))
            {
                continue;
            }

            var decision = resolved.Eligibility.Decisions.FirstOrDefault(x =>
                string.Equals(x.SourceName, entry.Name, StringComparison.OrdinalIgnoreCase));
            if (decision?.State is not (SourceEligibilityState.Eligible or SourceEligibilityState.EligibleSecondary))
            {
                continue;
            }

            result.Add(new DiscoveredUrlCandidate
            {
                Url = canonical,
                SourceType = DiscoverySourceType.IndustryAssociation,
                SourceName = entry.Name,
                Depth = 0,
                IsIndustryRelevant = decision.CountsAsRelevantIndustrySource,
                IsSecondaryRegistry = decision.State == SourceEligibilityState.EligibleSecondary,
                EligibilityReasonCode = decision.ReasonCode,
                DiscoveryReasonCode = decision.ReasonCode
            });
        }

        return Task.FromResult<IReadOnlyList<DiscoveredUrlCandidate>>(result);
    }

    private static List<string> MergeIndustries(UrlDiscoveryContext context)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in context.Industries.Concat(context.IndustrySynonyms))
        {
            if (string.IsNullOrWhiteSpace(item) || !seen.Add(item.Trim()))
            {
                continue;
            }

            result.Add(item.Trim());
        }

        return result;
    }
}
