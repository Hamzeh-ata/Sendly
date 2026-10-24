using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Sendly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Infrastructure.Research;

public class PriorResearchUrlDiscoverySource : IUrlDiscoverySource
{
    private readonly ISendlyDbContext _db;

    public PriorResearchUrlDiscoverySource(ISendlyDbContext db)
    {
        _db = db;
    }

    public string Name => "PriorResearchUrls";

    public bool IsEnabled(UrlDiscoveryContext context) => context.EnablePriorResearchUrls;

    public async Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var projectScoped = await LoadProjectScopedAsync(context, cancellationToken);
        var crossProject = await LoadCrossProjectScopedAsync(context, cancellationToken);

        var result = new List<DiscoveredUrlCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxPrior = ResolveMaxPriorUrls(context);

        foreach (var url in projectScoped.Concat(crossProject))
        {
            if (result.Count >= maxPrior)
            {
                break;
            }

            var canonical = UrlCanonicalizer.Canonicalize(url);
            if (string.IsNullOrWhiteSpace(canonical) || !seen.Add(canonical))
            {
                continue;
            }

            var host = UrlCanonicalizer.ExtractDomain(canonical);
            if (SystemDirectoryCatalog.IsSeedOrDirectoryHost(host ?? canonical))
            {
                continue;
            }

            if (!CountryScopeHelper.HostMatchesCountries(host, context.Countries))
            {
                continue;
            }

            if (LooksLikeExcludedHost(host, context.Industries))
            {
                continue;
            }

            result.Add(new DiscoveredUrlCandidate
            {
                Url = canonical,
                SourceType = DiscoverySourceType.PriorResearch,
                SourceName = "Prior research database",
                Depth = 0
            });
        }

        return result;
    }

    private async Task<List<string>> LoadProjectScopedAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var take = Math.Max(context.MaxUrls * 2, 80);
        var projectId = context.ProjectId.ToString("D").ToUpperInvariant();
        var rejected = (int)ProspectStatus.Rejected;
        var query = _db.Prospects.AsNoTracking()
            .Where(x => x.ProjectId == context.ProjectId
                        && x.Status != ProspectStatus.Rejected
                        && ((x.Website != null && x.Website != "") || (x.SourceUrl != null && x.SourceUrl != "")));
        var prospects = await SqliteDateTimeOffsetQuery.TakeRecentByStoredUtcAsync(
            _db,
            query,
            x => x.UpdatedAtUtc,
            $"""
             SELECT * FROM Prospects
             WHERE ProjectId = {projectId}
               AND Status <> {rejected}
               AND ((Website IS NOT NULL AND Website <> '') OR (SourceUrl IS NOT NULL AND SourceUrl <> ''))
             ORDER BY UpdatedAtUtc DESC
             LIMIT {take}
             """,
            take,
            cancellationToken);

        var urls = new List<string>();
        foreach (var prospect in prospects)
        {
            if (!IsReusableProspect(prospect, context))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(prospect.Website))
            {
                urls.Add(prospect.Website);
            }

            if (!string.IsNullOrWhiteSpace(prospect.SourceUrl))
            {
                urls.Add(prospect.SourceUrl);
            }
        }

        return urls;
    }

    private async Task<List<string>> LoadCrossProjectScopedAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
    {
        if (context.Countries.Count == 0 || context.Industries.Count == 0)
        {
            return [];
        }

        var take = Math.Max(context.MaxUrls * 2, 100);
        var projectId = context.ProjectId.ToString("D").ToUpperInvariant();
        var rejected = (int)ProspectStatus.Rejected;
        var query = _db.Prospects.AsNoTracking()
            .Where(x => x.ProjectId != context.ProjectId
                        && x.Status != ProspectStatus.Rejected
                        && ((x.Website != null && x.Website != "") || (x.SourceUrl != null && x.SourceUrl != "")));
        var prospects = await SqliteDateTimeOffsetQuery.TakeRecentByStoredUtcAsync(
            _db,
            query,
            x => x.UpdatedAtUtc,
            $"""
             SELECT * FROM Prospects
             WHERE ProjectId <> {projectId}
               AND Status <> {rejected}
               AND ((Website IS NOT NULL AND Website <> '') OR (SourceUrl IS NOT NULL AND SourceUrl <> ''))
             ORDER BY UpdatedAtUtc DESC
             LIMIT {take}
             """,
            take,
            cancellationToken);

        var urls = new List<string>();
        foreach (var prospect in prospects)
        {
            if (!IsReusableProspect(prospect, context))
            {
                continue;
            }

            if (!MatchesCountryScope(prospect.Country, prospect.Website ?? prospect.SourceUrl, context.Countries))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(prospect.Website))
            {
                urls.Add(prospect.Website);
            }

            if (!string.IsNullOrWhiteSpace(prospect.SourceUrl))
            {
                urls.Add(prospect.SourceUrl);
            }
        }

        return urls;
    }

    private static bool IsReusableProspect(Prospect prospect, UrlDiscoveryContext context)
    {
        if (CompanyIdentityResolver.IsGenericName(prospect.CompanyName)
            || ProspectJunkClassifier.IsJunkOrganization(prospect.CompanyName, prospect.Website ?? prospect.SourceUrl))
        {
            return false;
        }

        if (prospect.Status is ProspectStatus.Rejected)
        {
            return false;
        }

        var host = UrlCanonicalizer.ExtractDomain(prospect.Website)
                   ?? UrlCanonicalizer.ExtractDomain(prospect.SourceUrl);
        if (LooksLikeExcludedHost(host, context.Industries))
        {
            return false;
        }

        if (context.Industries.Count == 0)
        {
            return true;
        }

        return MatchesIndustryScope(prospect, context.Industries)
               || LooksLikeIndustryUrl(prospect.Website ?? prospect.SourceUrl, context.Industries);
    }

    private static int ResolveMaxPriorUrls(UrlDiscoveryContext context)
    {
        var share = Math.Clamp(context.MaxPriorResearchSharePercent, 0, 80);
        var capped = Math.Max(8, context.MaxUrls * share / 100);
        return Math.Min(capped, Math.Max(20, context.MaxUrls / 2));
    }

    private static bool MatchesCountryScope(string? country, string? websiteOrSource, List<string> countries)
    {
        var host = UrlCanonicalizer.ExtractDomain(websiteOrSource);
        if (!CountryScopeHelper.HostMatchesCountries(host, countries))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(country)
            && !CountryScopeHelper.CountryMatches(country, countries))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesIndustryScope(Prospect prospect, List<string> industries)
    {
        return MatchesScope(prospect.Industry, industries)
               || MatchesScope(prospect.CompanyDescription, industries)
               || (IsEducationIndustryScope(industries) && MatchesLikeSchoolName(prospect.CompanyName, industries));
    }

    private static bool MatchesLikeSchoolName(string? companyName, List<string> industries)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return false;
        }

        if (MatchesScope(companyName, industries))
        {
            return true;
        }

        var lower = companyName.ToLowerInvariant();
        return lower.Contains("school", StringComparison.Ordinal)
               || lower.Contains("academy", StringComparison.Ordinal)
               || lower.Contains("prep", StringComparison.Ordinal)
               || lower.Contains("college", StringComparison.Ordinal)
               || lower.Contains("مدرسة", StringComparison.Ordinal)
               || lower.Contains("مدارس", StringComparison.Ordinal);
    }

    private static bool LooksLikeIndustryUrl(string? url, List<string> industries)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var haystack = url.ToLowerInvariant();
        if (MatchesScope(haystack, industries))
        {
            return true;
        }

        if (!IsEducationIndustryScope(industries))
        {
            return false;
        }

        return haystack.Contains("school", StringComparison.Ordinal)
               || haystack.Contains("academy", StringComparison.Ordinal)
               || haystack.Contains(".edu", StringComparison.Ordinal)
               || haystack.Contains("مدرسة", StringComparison.Ordinal);
    }

    private static bool IsEducationIndustryScope(List<string> industries)
    {
        foreach (var industry in industries)
        {
            if (string.IsNullOrWhiteSpace(industry))
            {
                continue;
            }

            if (industry.Contains("school", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("education", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("academy", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeExcludedHost(string? host, List<string> industries)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var lower = host.ToLowerInvariant();
        if (lower.Contains("cannabis", StringComparison.Ordinal)
            || lower.Contains("weed", StringComparison.Ordinal)
            || lower.Contains("cleaning", StringComparison.Ordinal)
            || lower.Contains("selectminds", StringComparison.Ordinal)
            || lower.Contains("referrals.", StringComparison.Ordinal)
            || lower.Contains("libcal.", StringComparison.Ordinal)
            || lower.EndsWith(".gov", StringComparison.Ordinal)
            || lower.Contains(".gov.", StringComparison.Ordinal))
        {
            return true;
        }

        var schoolFocused = industries.Any(x =>
            !string.IsNullOrWhiteSpace(x)
            && (x.Contains("school", StringComparison.OrdinalIgnoreCase)
                || x.Contains("education", StringComparison.OrdinalIgnoreCase)
                || x.Contains("academy", StringComparison.OrdinalIgnoreCase)));
        if (!schoolFocused)
        {
            return false;
        }

        if (lower.Contains("moms.com", StringComparison.Ordinal)
            || lower.Contains("charter.org", StringComparison.Ordinal) && !lower.Contains("school", StringComparison.Ordinal)
            || lower.Contains("issa.com", StringComparison.Ordinal)
            || lower.Contains("bscai.org", StringComparison.Ordinal)
            || lower.Contains("fticonsulting", StringComparison.Ordinal)
            || lower.Contains("opencorporates", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool MatchesScope(string? value, List<string> scope)
    {
        if (string.IsNullOrWhiteSpace(value) || scope.Count == 0)
        {
            return false;
        }

        foreach (var item in scope)
        {
            if (string.IsNullOrWhiteSpace(item) || item.Length < 3)
            {
                continue;
            }

            if (value.Contains(item, StringComparison.OrdinalIgnoreCase)
                || item.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
