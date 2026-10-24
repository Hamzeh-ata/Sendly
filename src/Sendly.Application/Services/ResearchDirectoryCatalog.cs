using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public static class ResearchDirectoryCatalog
{
    private static readonly DirectorySource[] Sources =
    [
        new(
            ["Saudi Arabia", "SA", "KSA"],
            ["Education", "School", "Schools", "Private School"],
            [
                "madares.sa",
                "parents.madares.sa",
                "ksaschools.com",
                "yaschools.com",
                "educationmarket.sa"
            ],
            [
                "site:madares.sa",
                "site:parents.madares.sa",
                "site:parents.madares.sa/school-search",
                "site:ksaschools.com",
                "site:yaschools.com"
            ])
    ];

    public static IReadOnlyList<string> ResolveDirectoryHosts(ProspectSourceContext context)
    {
        var hosts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in MatchSources(context))
        {
            foreach (var host in source.Hosts)
            {
                if (seen.Add(host))
                {
                    hosts.Add(host);
                }
            }
        }

        return hosts;
    }

    public static IReadOnlyList<string> ResolveDirectorySiteQueries(ProspectSourceContext context)
    {
        var queries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in MatchSources(context))
        {
            foreach (var prefix in source.SiteQueryPrefixes)
            {
                if (seen.Add(prefix))
                {
                    queries.Add(prefix);
                }
            }
        }

        return queries;
    }

    public static bool IsDirectoryHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalized = host.Trim().ToLowerInvariant();
        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        foreach (var source in Sources)
        {
            foreach (var known in source.Hosts)
            {
                if (normalized.Equals(known, StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith("." + known, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsDirectoryUrl(Uri uri)
    {
        return IsDirectoryHost(uri.Host);
    }

    public static string BuildSeenKey(Uri uri)
    {
        if (!IsDirectoryUrl(uri))
        {
            var website = ProspectNormalizer.NormalizeWebsite(uri.ToString());
            return website ?? uri.Host.ToLowerInvariant();
        }

        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            return uri.Host.ToLowerInvariant();
        }

        return $"{uri.Host.ToLowerInvariant()}/{path.ToLowerInvariant()}";
    }

    public static string BuildCandidateKey(Uri uri, string? companyName)
    {
        if (IsDirectoryUrl(uri))
        {
            return $"dir:{BuildSeenKey(uri)}";
        }

        var website = ProspectNormalizer.NormalizeWebsite(uri.ToString());
        if (!string.IsNullOrWhiteSpace(website))
        {
            return $"site:{website}";
        }

        var company = ProspectNormalizer.NormalizeCompanyName(companyName);
        return $"name:{company}";
    }

    public static string? InferDirectoryCompanyName(string? title, Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            var cleaned = Regex.Replace(title, @"\s*[\|\-–:]\s*Madares.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            cleaned = cleaned.Split('|', '-', '–', ':')[0].Trim();
            if (!string.IsNullOrWhiteSpace(cleaned)
                && cleaned.Length >= 2
                && !cleaned.Equals("madares", StringComparison.OrdinalIgnoreCase)
                && !cleaned.Equals("school search", StringComparison.OrdinalIgnoreCase))
            {
                return cleaned;
            }
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
        {
            var last = segments[^1].Replace('-', ' ').Replace('_', ' ').Trim();
            if (last.Length >= 2 && !int.TryParse(last, out _))
            {
                return last;
            }
        }

        return null;
    }

    private static IEnumerable<DirectorySource> MatchSources(ProspectSourceContext context)
    {
        var countries = context.Countries
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        var industries = context.Industries
            .Concat(context.Keywords)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        foreach (var source in Sources)
        {
            if (!MatchesAny(countries, source.Countries) && countries.Count > 0)
            {
                continue;
            }

            if (!IsSchoolish(industries) && !MatchesAny(industries, source.IndustryHints))
            {
                continue;
            }

            yield return source;
        }
    }

    private static readonly string[] NonEducationSchoolPhrases =
    [
        "sailing school",
        "sail school",
        "dive school",
        "diving school",
        "scuba school",
        "ski school",
        "surf school",
        "surfing school",
        "swim school",
        "swimming school",
        "driving school",
        "flight school",
        "cooking school",
        "culinary school",
        "dance school",
        "music school",
        "yoga school",
        "martial arts school"
    ];

    private static bool IsSchoolish(IReadOnlyList<string> terms)
    {
        foreach (var term in terms)
        {
            var normalized = StripNonEducationSchoolPhrases(term);
            if (ContainsWholeWord(normalized, "school")
                || normalized.Contains("education", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("مدرسة", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("تعليم", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string StripNonEducationSchoolPhrases(string value)
    {
        var normalized = value;
        foreach (var phrase in NonEducationSchoolPhrases)
        {
            normalized = Regex.Replace(
                normalized,
                $@"\b{Regex.Escape(phrase)}s?\b",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return normalized;
    }

    private static bool ContainsWholeWord(string value, string word)
    {
        return Regex.IsMatch(
            value,
            $@"\b{Regex.Escape(word)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool MatchesAny(IReadOnlyList<string> values, IReadOnlyList<string> candidates)
    {
        foreach (var value in values)
        {
            foreach (var candidate in candidates)
            {
                if (value.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                    || value.Contains(candidate, StringComparison.OrdinalIgnoreCase)
                    || candidate.Contains(value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private sealed record DirectorySource(
        string[] Countries,
        string[] IndustryHints,
        string[] Hosts,
        string[] SiteQueryPrefixes);
}
