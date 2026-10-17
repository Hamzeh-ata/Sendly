namespace Sendly.Domain.Services;

public static class WebSearchHitRanker
{
    public static List<T> RankAndFilter<T>(
        IEnumerable<T> hits,
        Func<T, string> urlSelector,
        Func<T, string?> titleSelector,
        Func<T, string?> snippetSelector,
        IReadOnlyList<string> keywords,
        IReadOnlyList<string> industries,
        IReadOnlyList<string> countries,
        IReadOnlyList<string> excludedKeywords,
        IReadOnlyList<string> hostDenylist,
        Action<T, int>? assignScore)
    {
        var scored = new List<(T Hit, int Score, string Host)>();
        foreach (var hit in hits)
        {
            var url = urlSelector(hit);
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (IsDeniedHost(uri.Host, hostDenylist))
            {
                continue;
            }

            var text = string.Join(
                ' ',
                new[] { titleSelector(hit), snippetSelector(hit), uri.Host, uri.AbsolutePath }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));

            if (ContainsAny(text, excludedKeywords))
            {
                continue;
            }

            var score = 0;
            score += CountMatches(text, keywords) * 10;
            score += CountMatches(text, industries) * 8;
            score += CountMatches(text, countries) * 6;
            if (uri.AbsolutePath.Contains("contact", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.Contains("about", StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }

            assignScore?.Invoke(hit, score);
            scored.Add((hit, score, uri.Host.ToLowerInvariant()));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Host)
            .Select(x => x.Hit)
            .ToList();
    }

    private static bool IsDeniedHost(string host, IReadOnlyList<string> denylist)
    {
        foreach (var denied in denylist)
        {
            if (string.IsNullOrWhiteSpace(denied))
            {
                continue;
            }

            if (host.Equals(denied, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + denied.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && text.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountMatches(string text, IReadOnlyList<string> values)
    {
        var count = 0;
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && text.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }
}
