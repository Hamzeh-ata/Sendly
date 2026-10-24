using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Infrastructure.Research;

public class DirectoryCatalogDiscoverySource : IUrlDiscoverySource
{
    private readonly DiscoverySourceRegistryService _registry;

    public DirectoryCatalogDiscoverySource(DiscoverySourceRegistryService registry)
    {
        _registry = registry;
    }

    public string Name => "DirectoryCatalog";

    public bool IsEnabled(UrlDiscoveryContext context) => context.EnableDirectoryDiscovery;

    public async Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var industries = MergeIndustries(context);
        var resolved = await _registry.ResolveEligibleWithDecisionsAsync(
            context.Countries,
            industries,
            cancellationToken);

        context.LastSourceEligibility = resolved.Eligibility;
        foreach (var decision in resolved.Decisions)
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

        foreach (var source in resolved.Sources)
        {
            resolved.SourceDecisions.TryGetValue(source.Id, out var decision);
            var isPrimary = decision?.CountsAsRelevantIndustrySource == true;
            var isSecondary = decision?.State == SourceEligibilityState.EligibleSecondary;
            var seedUrls = ResolveSeedUrls(source.NormalizedBaseUrl, source.Name, context.Countries);
            var admittedForSource = 0;
            foreach (var seedUrl in seedUrls)
            {
                var canonical = UrlCanonicalizer.Canonicalize(seedUrl);
                if (string.IsNullOrWhiteSpace(canonical) || !seen.Add(canonical))
                {
                    continue;
                }

                result.Add(new DiscoveredUrlCandidate
                {
                    Url = canonical,
                    SourceType = source.SourceType == DiscoverySourceType.IndustryAssociation
                        ? DiscoverySourceType.IndustryAssociation
                        : DiscoverySourceType.DirectoryCatalog,
                    SourceName = source.Name,
                    DiscoverySourceId = source.Id,
                    Depth = 0,
                    IsIndustryRelevant = isPrimary,
                    IsSecondaryRegistry = isSecondary,
                    EligibilityReasonCode = decision?.ReasonCode,
                    DiscoveryReasonCode = decision?.ReasonCode ?? "discovery.directory_catalog",
                    PriorityBoost = admittedForSource == 0 ? 20 : 12
                });
                admittedForSource++;
            }

            await _registry.RecordAttemptAsync(
                source.Id,
                succeeded: true,
                candidates: admittedForSource,
                error: null,
                cancellationToken);
        }

        if (result.Count > 0)
        {
            return result;
        }

        var fallback = SystemDirectoryCatalog.ResolveWithDecisions(context.Countries, industries);
        MergeEligibility(context, fallback.Eligibility);
        foreach (var entry in fallback.Entries)
        {
            var decision = fallback.Eligibility.Decisions.FirstOrDefault(x =>
                string.Equals(x.SourceUrl, entry.BaseUrl, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.SourceName, entry.Name, StringComparison.OrdinalIgnoreCase));
            var seedUrls = entry.PreferredListingUrls.Count > 0
                ? entry.PreferredListingUrls
                : [entry.BaseUrl];
            var admittedForSource = 0;
            foreach (var seedUrl in seedUrls)
            {
                var canonical = UrlCanonicalizer.Canonicalize(seedUrl);
                if (string.IsNullOrWhiteSpace(canonical) || !seen.Add(canonical))
                {
                    continue;
                }

                result.Add(new DiscoveredUrlCandidate
                {
                    Url = canonical,
                    SourceType = DiscoverySourceType.DirectoryCatalog,
                    SourceName = entry.Name,
                    Depth = 0,
                    IsIndustryRelevant = decision?.CountsAsRelevantIndustrySource == true,
                    IsSecondaryRegistry = decision?.State == SourceEligibilityState.EligibleSecondary,
                    EligibilityReasonCode = decision?.ReasonCode,
                    DiscoveryReasonCode = decision?.ReasonCode ?? "discovery.directory_catalog_fallback",
                    PriorityBoost = admittedForSource == 0 ? 20 : 12
                });
                admittedForSource++;
            }
        }

        return result;
    }

    private static List<string> ResolveSeedUrls(
        string? baseUrl,
        string sourceName,
        IReadOnlyList<string> countries)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in SystemDirectoryCatalog.AllEntries())
        {
            if (!string.Equals(entry.Name, sourceName, StringComparison.OrdinalIgnoreCase)
                && !HostsMatch(entry.BaseUrl, baseUrl))
            {
                continue;
            }

            if (entry.PreferredListingUrls.Count > 0)
            {
                foreach (var preferred in entry.PreferredListingUrls)
                {
                    if (seen.Add(preferred))
                    {
                        result.Add(preferred);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(entry.BaseUrl) && seen.Add(entry.BaseUrl))
            {
                result.Add(entry.BaseUrl);
            }
        }

        if (result.Count == 0 && !string.IsNullOrWhiteSpace(baseUrl) && seen.Add(baseUrl))
        {
            result.Add(baseUrl);
        }

        if (result.Count == 0)
        {
            return result;
        }

        if (!IsUnitedStatesQuery(countries))
        {
            return result
                .Where(url => !LooksLikeUsStateListing(url) || HostsMatch(url, baseUrl))
                .DefaultIfEmpty(result[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return result;
    }

    private static bool IsUnitedStatesQuery(IReadOnlyList<string> countries)
    {
        foreach (var country in countries)
        {
            if (CountryScopeHelper.CountryMatches(country, ["United States", "USA", "US"]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeUsStateListing(string url)
    {
        var lower = url.ToLowerInvariant();
        return lower.Contains("privateschoolreview.com/california", StringComparison.Ordinal)
               || lower.Contains("privateschoolreview.com/texas", StringComparison.Ordinal)
               || lower.Contains("privateschoolreview.com/new-york", StringComparison.Ordinal)
               || lower.Contains("privateschoolreview.com/florida", StringComparison.Ordinal);
    }

    private static bool HostsMatch(string? left, string? right)
    {
        var leftHost = UrlCanonicalizer.ExtractDomain(left);
        var rightHost = UrlCanonicalizer.ExtractDomain(right);
        return !string.IsNullOrWhiteSpace(leftHost)
               && !string.IsNullOrWhiteSpace(rightHost)
               && leftHost.Equals(rightHost, StringComparison.OrdinalIgnoreCase);
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

    private static void MergeEligibility(UrlDiscoveryContext context, SourceEligibilityResolveResult eligibility)
    {
        context.LastSourceEligibility = eligibility;
        foreach (var decision in eligibility.Decisions)
        {
            if (!context.SourceEligibilityDecisions.Any(x =>
                    string.Equals(x.SourceName, decision.SourceName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.SourceUrl, decision.SourceUrl, StringComparison.OrdinalIgnoreCase)))
            {
                context.SourceEligibilityDecisions.Add(decision);
            }
        }
    }
}
