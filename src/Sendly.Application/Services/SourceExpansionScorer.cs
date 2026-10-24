namespace Sendly.Application.Services;

public class SourceExpansionScore
{
    public int Total { get; set; }

    public int UrlSignal { get; set; }

    public int LinkTextSignal { get; set; }

    public int ContextSignal { get; set; }

    public int IndustrySignal { get; set; }

    public int GeographySignal { get; set; }

    public int ContentSignal { get; set; }

    public bool LooksPromising => Total >= 25;
}

public static class SourceExpansionScorer
{
    private static readonly string[] DirectorySignals =
    [
        "member", "members", "directory", "listing", "companies", "association",
        "chamber", "registry", "membership", "find a", "our members",
        "pedia", "encyclopedia", "school guide", "schoolguide", "compare schools",
        "أعضاء", "دليل", "سجل", "شركات", "غرفة", "موسوعة"
    ];

    private static readonly string[] ProfileSignals =
    [
        "company profile", "business profile", "about the company", "contact us",
        "ملف الشركة", "عن الشركة", "تواصل"
    ];

    public static SourceExpansionScore Score(
        string url,
        string? linkText,
        string? surroundingContext,
        string? pageTitle,
        string? pageExcerpt,
        IReadOnlyList<string> industries,
        IReadOnlyList<string> countries)
    {
        var score = new SourceExpansionScore();
        var urlHaystack = Normalize(url);
        var textHaystack = Normalize($"{linkText} {surroundingContext} {pageTitle}");
        var contentHaystack = Normalize(pageExcerpt);

        score.UrlSignal = CountHits(urlHaystack, DirectorySignals) * 8;
        score.LinkTextSignal = CountHits(textHaystack, DirectorySignals) * 10;
        score.ContextSignal = CountHits(textHaystack, ProfileSignals) * 4;
        score.ContentSignal = CountHits(contentHaystack, DirectorySignals) * 6
                              + CountHits(contentHaystack, ProfileSignals) * 3;
        score.IndustrySignal = CountListHits(urlHaystack + " " + textHaystack + " " + contentHaystack, industries) * 5;
        score.GeographySignal = CountListHits(urlHaystack + " " + textHaystack + " " + contentHaystack, countries) * 4;

        score.Total = score.UrlSignal
                      + score.LinkTextSignal
                      + score.ContextSignal
                      + score.ContentSignal
                      + score.IndustrySignal
                      + score.GeographySignal;
        return score;
    }

    public static bool LooksLikeDirectory(string url)
        => Score(url, null, null, null, null, [], []).UrlSignal > 0;

    private static int CountHits(string haystack, IEnumerable<string> signals)
    {
        var count = 0;
        foreach (var signal in signals)
        {
            if (haystack.Contains(Normalize(signal), StringComparison.Ordinal))
            {
                count++;
            }
        }

        return Math.Min(count, 4);
    }

    private static int CountListHits(string haystack, IReadOnlyList<string> values)
    {
        var count = 0;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var token = Normalize(value);
            if (token.Length >= 3 && haystack.Contains(token, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return Math.Min(count, 3);
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
