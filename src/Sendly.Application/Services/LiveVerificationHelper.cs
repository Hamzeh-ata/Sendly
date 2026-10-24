namespace Sendly.Application.Services;

public static class LiveVerificationHelper
{
    public static bool SupportsCompanyIdentity(string companyName, string? title, string? excerpt)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return false;
        }

        var haystack = $"{title} {excerpt}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return false;
        }

        var tokens = companyName
            .Split([' ', '-', '|', ',', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3)
            .Select(x => x.ToLowerInvariant())
            .Take(4)
            .ToList();

        if (tokens.Count == 0)
        {
            return haystack.Contains(companyName.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        }

        var hits = tokens.Count(token => haystack.Contains(token, StringComparison.Ordinal));
        return hits >= Math.Max(1, tokens.Count / 2);
    }

    public static List<string> PrioritizeContactPages(IEnumerable<string> links)
    {
        var hints = AdaptiveDiscoveryRecovery.ContactPageHints();
        return links
            .OrderByDescending(link => ScoreContactLink(link, hints))
            .ThenBy(link => link.Length)
            .ToList();
    }

    private static int ScoreContactLink(string link, IReadOnlyList<string> hints)
    {
        var haystack = link.ToLowerInvariant();
        var score = 0;
        foreach (var hint in hints)
        {
            if (!haystack.Contains(hint, StringComparison.Ordinal))
            {
                continue;
            }

            score += hint.StartsWith("contact", StringComparison.Ordinal) || hint.Contains("تواصل", StringComparison.Ordinal)
                ? 10
                : 3;
        }

        return score;
    }
}
