using System.Net;
using System.Text.RegularExpressions;

namespace Sendly.Domain.Services;

public static class HtmlToPlainTextConverter
{
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LineBreakRegex = new(@"<(br|/p|/div|/li)\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MultiSpaceRegex = new(@"[ \t]+", RegexOptions.Compiled);
    private static readonly Regex MultiNewlineRegex = new(@"\n{3,}", RegexOptions.Compiled);

    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withBreaks = LineBreakRegex.Replace(html, "\n");
        var withoutTags = TagRegex.Replace(withBreaks, string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var collapsedSpaces = MultiSpaceRegex.Replace(decoded, " ");
        var collapsedNewlines = MultiNewlineRegex.Replace(collapsedSpaces, "\n\n");
        return collapsedNewlines.Trim();
    }
}
