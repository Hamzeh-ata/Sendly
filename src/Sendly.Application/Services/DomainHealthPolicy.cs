using System.Text.RegularExpressions;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public static class DomainHealthPolicy
{
    private static readonly Regex NumericHostRegex = new(
        @"^\d{2,}\.[a-z]{2,}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ParkingHostHints =
    [
        "0007.ae", "sedo.com", "dan.com", "afternic.com", "hugedomains.com",
        "godaddy.com", "spaceship.com", "namecheap.com", "squadhelp.com",
        "atom.com", "brandbucket.com"
    ];

    private static readonly string[] MarketplaceSaleHints =
    [
        "for sale", "domain is for sale", "buy this domain", "make offer",
        "minimum price", "this domain", "parked domain", "domain parking"
    ];

    public static bool IsParkingOrMarketplaceHost(string? hostOrUrl)
    {
        var host = UrlCanonicalizer.ExtractDomain(hostOrUrl) ?? hostOrUrl;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var lower = host.Trim().ToLowerInvariant();
        if (NumericHostRegex.IsMatch(lower))
        {
            return true;
        }

        foreach (var hint in ParkingHostHints)
        {
            if (lower.Equals(hint, StringComparison.OrdinalIgnoreCase)
                || lower.EndsWith("." + hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeDomainSalePage(string? title, string? excerpt, string? url)
    {
        var haystack = $"{title} {excerpt} {url}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return false;
        }

        foreach (var hint in MarketplaceSaleHints)
        {
            if (haystack.Contains(hint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HostHasRepeatedFailures(
        string? hostOrUrl,
        IReadOnlyDictionary<string, int>? hostFailureCounts,
        int threshold = 3)
    {
        var host = UrlCanonicalizer.ExtractDomain(hostOrUrl) ?? hostOrUrl;
        if (string.IsNullOrWhiteSpace(host) || hostFailureCounts is null)
        {
            return false;
        }

        return hostFailureCounts.TryGetValue(host, out var count) && count >= threshold;
    }

    public static bool ShouldRejectArchiveCandidate(
        string? url,
        IReadOnlyList<string> industries,
        IReadOnlyDictionary<string, int>? hostFailureCounts)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        if (IsParkingOrMarketplaceHost(url))
        {
            return true;
        }

        if (HostHasRepeatedFailures(url, hostFailureCounts))
        {
            return true;
        }

        return !UrlLooksIndustryRelevant(url, industries);
    }

    public static bool UrlLooksIndustryRelevant(string? url, IReadOnlyList<string> industries)
    {
        if (string.IsNullOrWhiteSpace(url) || industries.Count == 0)
        {
            return false;
        }

        var haystack = url.Replace('-', ' ').Replace('_', ' ').Replace('/', ' ');
        foreach (var industry in industries)
        {
            if (string.IsNullOrWhiteSpace(industry) || industry.Length < 3)
            {
                continue;
            }

            var compact = industry.Trim().Replace('-', ' ');
            if (haystack.Contains(compact, StringComparison.OrdinalIgnoreCase)
                || haystack.Contains(compact.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var token in compact.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length < 5)
                {
                    continue;
                }

                if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
