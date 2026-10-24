using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services.WebsiteInvestigation;

public static class PageContentAnalyzer
{
    private static readonly string[] CategoryHints =
    [
        "category", "categories", "directory", "listings", "schools", "companies", "browse",
        "تصنيف", "فئة", "فئات", "دليل", "مدارس", "شركات", "قائمة"
    ];

    private static readonly string[] ProfileHints =
    [
        "profile", "school/", "company/", "institution", "listing/", "details", "view/",
        "ملف", "مدرسة", "شركة", "مؤسسة", "تفاصيل"
    ];

    private static readonly string[] ContactHints =
    [
        "contact", "get-in-touch", "reach-us", "enquiry", "inquiry", "تواصل", "اتصل", "اتصل-بنا"
    ];

    private static readonly string[] AboutHints =
    [
        "about", "about-us", "who-we-are", "our-story", "عن", "من-نحن", "نبذة"
    ];

    private static readonly string[] TeamHints =
    [
        "team", "leadership", "management", "staff", "فريق", "إدارة", "قيادة"
    ];

    private static readonly string[] LocationHints =
    [
        "location", "locations", "campus", "campuses", "branch", "branches", "موقع", "فروع", "حرم"
    ];

    private static readonly string[] NewsHints =
    [
        "news", "announcement", "blog", "press", "أخبار", "إعلان", "مدونة"
    ];

    private static readonly string[] ServicesHints =
    [
        "service", "services", "program", "programs", "activity", "activities", "admission",
        "خدمات", "برامج", "أنشطة", "قبول", "تسجيل"
    ];

    private static readonly string[] PaginationHints =
    [
        "page=", "p=", "/page/", "offset=", "التالي", "السابق", "next", "prev"
    ];

    public static InvestigationPageKind Classify(
        string pageUrl,
        ExtractedPageData extracted,
        UrlPagePurpose urlPurpose,
        bool isDirectoryHost)
    {
        var path = TryGetPath(pageUrl);
        var haystack = BuildHaystack(pageUrl, path, extracted);
        var listingSignals = CountListingSignals(extracted);

        if (ContainsAny(haystack, PaginationHints) && listingSignals >= 2)
        {
            return InvestigationPageKind.Pagination;
        }

        if (urlPurpose == UrlPagePurpose.Contact || ContainsAny(haystack, ContactHints))
        {
            return InvestigationPageKind.Contact;
        }

        if (urlPurpose == UrlPagePurpose.About || ContainsAny(haystack, AboutHints))
        {
            return InvestigationPageKind.About;
        }

        if (urlPurpose == UrlPagePurpose.Team || ContainsAny(haystack, TeamHints))
        {
            return InvestigationPageKind.Team;
        }

        if (urlPurpose == UrlPagePurpose.Location || ContainsAny(haystack, LocationHints))
        {
            return InvestigationPageKind.Location;
        }

        if (ContainsAny(haystack, NewsHints))
        {
            return InvestigationPageKind.News;
        }

        if (ContainsAny(haystack, ServicesHints))
        {
            return InvestigationPageKind.Services;
        }

        if (ContainsAny(haystack, CategoryHints) || listingSignals >= 3)
        {
            return listingSignals >= 4 ? InvestigationPageKind.Listing : InvestigationPageKind.Category;
        }

        if (isDirectoryHost
            || urlPurpose == UrlPagePurpose.DirectoryListing
            || ContainsAny(haystack, ProfileHints)
            || (isDirectoryHost && LooksLikeProfilePath(path)))
        {
            if (ContainsAny(haystack, ProfileHints) || LooksLikeProfilePath(path))
            {
                return InvestigationPageKind.Profile;
            }
        }

        if (listingSignals >= 4)
        {
            return InvestigationPageKind.Listing;
        }

        if (LooksLikeProfilePath(path)
            && (isDirectoryHost || urlPurpose == UrlPagePurpose.DirectoryListing || ContainsAny(haystack, ProfileHints)))
        {
            return InvestigationPageKind.Profile;
        }

        if (path is "/" or "" || path.Count(c => c == '/') <= 1)
        {
            return InvestigationPageKind.Home;
        }

        return InvestigationPageKind.Unknown;
    }

    public static InvestigationSiteRole InferSiteRole(
        bool isDirectorySeed,
        bool isDirectoryHost,
        InvestigationPageKind pageKind,
        int listingCount,
        UrlPagePurpose urlPurpose,
        DiscoverySourceType sourceType)
    {
        if (isDirectorySeed
            || isDirectoryHost
            || urlPurpose == UrlPagePurpose.DirectoryListing
            || sourceType is DiscoverySourceType.DirectoryCatalog or DiscoverySourceType.IndustryAssociation)
        {
            return InvestigationSiteRole.Directory;
        }

        if (listingCount >= 3
            || pageKind is InvestigationPageKind.Category or InvestigationPageKind.Listing or InvestigationPageKind.Pagination)
        {
            return InvestigationSiteRole.Directory;
        }

        return InvestigationSiteRole.Organization;
    }

    public static bool IsInternalInvestigationTarget(InvestigationPageKind kind)
        => kind is InvestigationPageKind.Category
            or InvestigationPageKind.Listing
            or InvestigationPageKind.Profile
            or InvestigationPageKind.Contact
            or InvestigationPageKind.About
            or InvestigationPageKind.Team
            or InvestigationPageKind.Location
            or InvestigationPageKind.Services
            or InvestigationPageKind.Pagination;

    public static InvestigationPageKind ClassifyLink(string url, bool isDirectoryHost)
    {
        var path = TryGetPath(url);
        var haystack = (url + " " + path).ToLowerInvariant();

        if (ContainsAny(haystack, PaginationHints))
        {
            return InvestigationPageKind.Pagination;
        }

        if (ContainsAny(haystack, ContactHints))
        {
            return InvestigationPageKind.Contact;
        }

        if (ContainsAny(haystack, AboutHints))
        {
            return InvestigationPageKind.About;
        }

        if (ContainsAny(haystack, TeamHints))
        {
            return InvestigationPageKind.Team;
        }

        if (ContainsAny(haystack, LocationHints))
        {
            return InvestigationPageKind.Location;
        }

        if (ContainsAny(haystack, CategoryHints))
        {
            return InvestigationPageKind.Category;
        }

        if (isDirectoryHost && (ContainsAny(haystack, ProfileHints) || LooksLikeProfilePath(path)))
        {
            return InvestigationPageKind.Profile;
        }

        if (ContainsAny(haystack, ServicesHints))
        {
            return InvestigationPageKind.Services;
        }

        return InvestigationPageKind.Unknown;
    }

    private static int CountListingSignals(ExtractedPageData extracted)
    {
        var count = 0;
        if (extracted.SameOriginLinks.Count >= 8)
        {
            count += 2;
        }

        if (extracted.SameOriginLinks.Count >= 15)
        {
            count += 1;
        }

        if (extracted.ExternalLinks.Count >= 3)
        {
            count += 1;
        }

        var text = extracted.Text ?? string.Empty;
        if (text.Contains("card", StringComparison.OrdinalIgnoreCase)
            || text.Contains("listing", StringComparison.OrdinalIgnoreCase)
            || text.Contains("مدرسة", StringComparison.OrdinalIgnoreCase)
            || text.Contains("شركة", StringComparison.OrdinalIgnoreCase))
        {
            count += 1;
        }

        return count;
    }

    private static bool LooksLikeProfilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path is "/" or "")
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2;
    }

    private static string TryGetPath(string pageUrl)
    {
        return Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath.ToLowerInvariant()
            : string.Empty;
    }

    private static string BuildHaystack(string pageUrl, string path, ExtractedPageData extracted)
    {
        return string.Join(
            ' ',
            pageUrl,
            path,
            extracted.Title ?? string.Empty,
            extracted.PageHeading ?? string.Empty,
            extracted.Excerpt ?? string.Empty).ToLowerInvariant();
    }

    private static bool ContainsAny(string haystack, IEnumerable<string> hints)
    {
        foreach (var hint in hints)
        {
            if (haystack.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeClientRenderedShell(string? html, ExtractedPageData extracted)
    {
        var anchorCount = extracted.SameOriginLinks.Count + extracted.ExternalLinks.Count;
        if (anchorCount >= 10)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            return anchorCount == 0;
        }

        var lower = html.ToLowerInvariant();
        var spaMarkers = lower.Contains("<app-root", StringComparison.Ordinal)
                         || lower.Contains("ng-version", StringComparison.Ordinal)
                         || lower.Contains("data-reactroot", StringComparison.Ordinal)
                         || lower.Contains("__next_data__", StringComparison.Ordinal)
                         || lower.Contains("id=\"root\"", StringComparison.Ordinal)
                         || lower.Contains("id='root'", StringComparison.Ordinal);

        if (!spaMarkers)
        {
            return false;
        }

        return anchorCount <= 3;
    }

    public static List<string> HarvestEmbeddedAbsoluteUrls(string? html, Uri pageUrl, int max = 40)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = System.Text.RegularExpressions.Regex.Matches(
            html,
            @"https?://[^\s""'<>\\]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (results.Count >= max)
            {
                break;
            }

            var raw = match.Value.Trim().TrimEnd('.', ',', ';', ')', ']');
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
            {
                continue;
            }

            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            if (absolute.Host.Contains("googleapis.", StringComparison.OrdinalIgnoreCase)
                || absolute.Host.Contains("gstatic.", StringComparison.OrdinalIgnoreCase)
                || absolute.Host.Contains("cloudflare.", StringComparison.OrdinalIgnoreCase)
                || absolute.AbsolutePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || absolute.AbsolutePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                || absolute.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || absolute.AbsolutePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || absolute.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                || absolute.AbsolutePath.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
                || absolute.AbsolutePath.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var canonical = absolute.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (!seen.Add(canonical))
            {
                continue;
            }

            if (UrlCanonicalizer.AreSameRegistrableDomain(pageUrl.Host, absolute.Host)
                || SystemDirectoryCatalog.IsSeedOrDirectoryHost(absolute.Host))
            {
                results.Add(absolute.ToString());
            }
        }

        return results;
    }
}
