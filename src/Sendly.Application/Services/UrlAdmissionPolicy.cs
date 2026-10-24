using System.Net;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public enum UrlPagePurpose
{
    Unknown = 0,
    Contact = 1,
    About = 2,
    Team = 3,
    Location = 4,
    CompanyProfile = 5,
    FranchiseLocation = 6,
    DirectoryListing = 7,
    AssetOrApi = 8,
    JobListing = 9,
    Navigation = 10,
    Listicle = 11
}

public class UrlAdmissionDecision
{
    public bool Accepted { get; set; }

    public string ReasonCode { get; set; } = "url.accepted";

    public string Explanation { get; set; } = string.Empty;

    public UrlPagePurpose PagePurpose { get; set; } = UrlPagePurpose.Unknown;

    public string? NormalizedUrl { get; set; }

    public string? NormalizedDomain { get; set; }
}

public static class UrlAdmissionPolicy
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".ico", ".bmp", ".tif", ".tiff",
        ".mp4", ".webm", ".avi", ".mov", ".mkv", ".mp3", ".wav", ".ogg", ".flac",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".css", ".map", ".pdf", ".zip", ".rar", ".7z", ".gz", ".tar",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".xml", ".json", ".txt", ".csv", ".rss", ".atom", ".yaml", ".yml"
    };

    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx"
    };

    private static readonly string[] ApiPathTokens =
    [
        "/wp-json/", "/api/", "/graphql", "/oembed", "/cdn-cgi/", "/_next/static/",
        "/static/chunks/", "/assets/", "/.well-known/", "/feed/", "/rss",
        "/_payload.json", "/payload.json", "/xmlrpc.php", "/xmlrpc",
        "/wp-cron.php", "/wp-login.php", "/wp-admin/", "/comments/feed"
    ];

    private static readonly string[] LowValuePathTokens =
    [
        "/privacy", "/privacy-policy", "/user-privacy", "/cookie", "/terms",
        "/terms-of-use", "/legal-information", "/legal-entity", "/impressum"
    ];

    private static readonly string[] RegistryChromePathTokens =
    [
        "/pricing", "/advanced-search", "/use-cases", "/newsletter", "/partners",
        "/purpose", "/impact", "/plug-in", "/legal", "/feed", "/rss",
        "/comments/feed", "/case-studies", "/insights-and-events"
    ];

    private static readonly string[] GenericRegistryHosts =
    [
        "opencorporates.com", "companieshouse.gov.uk", "sec.gov"
    ];

    private static readonly string[] LowValueHostHints =
    [
        "linktr.ee", "linktree.com", "hostingersite.com", "hostinger.com",
        "000webhostapp.com", "github.io", "blogspot.com", "sites.google.com",
        "wixsite.com", "weebly.com", "square.site"
    ];

    private static readonly string[] TrackingTokens =
    [
        "utm_", "fbclid=", "gclid=", "mc_cid=", "mc_eid=", "/pixel", "/tracking",
        "doubleclick", "googletagmanager", "google-analytics", "hotjar", "segment.io"
    ];

    private static readonly string[] EmbedHostHints =
    [
        "youtube.com", "youtu.be", "vimeo.com", "player.vimeo.com", "maps.google.com",
        "google.com/maps", "openstreetmap.org", "platform.twitter.com", "syndication.twitter.com"
    ];

    private static readonly string[] CdnHostHints =
    [
        "images.squarespace-cdn.com", "cdn.shopify.com", "cloudfront.net", "imgix.net",
        "akamaihd.net", "fastly.net", "googleapis.com", "gstatic.com", "bootstrapcdn.com"
    ];

    private static readonly string[] ContactHints =
    [
        "contact", "get-in-touch", "enquire", "enquir", "تواصل", "اتصل"
    ];

    private static readonly string[] AboutHints = ["about", "about-us", "our-story", "عنّا", "من-نحن"];

    private static readonly string[] TeamHints = ["team", "staff", "people", "our-people", "leadership"];

    private static readonly string[] LocationHints =
    [
        "location", "locations", "clubs", "club/", "branch", "branches", "studio", "studios",
        "find-us", "near-me", "campus", "centres", "centers"
    ];

    private static readonly string[] JobHints =
    [
        "/jobs", "/job/", "/careers", "/vacancies", "/employment", "/recruitment",
        "browse-jobs", "/opportunities", "/hiring"
    ];

    private static readonly string[] NavigationHints =
    [
        "/search", "/find-a-", "/find-a/", "find-a-school", "/sitemap", "/login", "/signin",
        "/sign-in", "/register", "/cart", "/checkout", "/sessions/new", "/request-password",
        "/request-signup", "/request-organization"
    ];

    private static readonly string[] AuthHostPrefixes =
    [
        "sso.", "login.", "auth.", "accounts.", "signin.", "id."
    ];

    private static readonly string[] AssociationOutboundNoiseHosts =
    [
        "closeup.org", "votervoice.net", "jobs.opencorporates.com"
    ];

    private static readonly string[] ListicleHints =
    [
        "/best-", "/top-", "/top-10", "/top-20", "best-schools", "best-companies",
        "/guide-to-", "/list-of-"
    ];

    private static readonly HashSet<string> HtmlContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/html",
        "application/xhtml+xml"
    };

    private static readonly string[] SocialHosts =
    [
        "facebook.com", "instagram.com", "linkedin.com", "twitter.com", "x.com",
        "youtube.com", "tiktok.com", "pinterest.com", "reddit.com", "snapchat.com"
    ];

    private static readonly string[] ForumPathTokens =
    [
        "/forum/", "/forums/", "/showthread", "/viewtopic", "/member.php",
        "/members/", "/community/profile", "/profile.php?u="
    ];

    private static readonly string[] AppStoreHosts =
    [
        "play.google.com", "apps.apple.com", "microsoftapps.com"
    ];

    public static UrlAdmissionDecision Evaluate(
        string? url,
        string? contentType = null,
        bool allowConfiguredApiProvider = false,
        IReadOnlyList<string>? countries = null,
        IReadOnlyList<string>? industries = null)
    {
        var decision = new UrlAdmissionDecision();
        if (string.IsNullOrWhiteSpace(url))
        {
            return Reject(decision, "url.empty", "URL is empty.");
        }

        var trimmedInput = url.Trim();
        if (ContainsPhpTemplateFragment(trimmedInput))
        {
            return Reject(decision, "url.php_template", "URL contains literal PHP/template code.");
        }

        if (HasUnresolvedUndefinedSegment(trimmedInput))
        {
            return Reject(decision, "url.undefined_segment", "URL contains an unresolved /undefined/ path segment.");
        }

        if (HasUnencodedWhitespace(trimmedInput))
        {
            return Reject(decision, "url.whitespace", "URL contains unencoded whitespace.");
        }

        var normalized = UrlCanonicalizer.Canonicalize(trimmedInput);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Reject(decision, "url.invalid", "URL could not be canonicalized.");
        }

        decision.NormalizedUrl = normalized;
        decision.NormalizedDomain = UrlCanonicalizer.ExtractDomain(normalized);

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Reject(decision, "url.scheme", "Only http/https URLs are admitted.");
        }

        if (SsrfGuard.IsBlockedHost(uri.Host)
            || (IPAddress.TryParse(uri.Host, out var literalIp) && SsrfGuard.IsBlockedAddress(literalIp)))
        {
            return Reject(
                decision,
                "url.ssrf_or_unsafe",
                "URL host resolves to a prohibited loopback, private, or metadata destination.");
        }

        if (!string.IsNullOrWhiteSpace(contentType) && !IsHtmlContentType(contentType))
        {
            return Reject(
                decision,
                "url.content_type",
                $"Content type '{contentType}' is not crawlable HTML.");
        }

        var path = uri.AbsolutePath ?? string.Empty;
        var full = normalized.ToLowerInvariant();
        var host = (uri.Host ?? string.Empty).ToLowerInvariant();
        var extension = GetExtension(path);

        if (HasUnresolvedUndefinedSegment(path) || HasUnresolvedUndefinedSegment(uri.OriginalString))
        {
            return Reject(decision, "url.undefined_segment", "URL contains an unresolved /undefined/ path segment.");
        }

        if (ContainsPhpTemplateFragment(normalized))
        {
            return Reject(decision, "url.php_template", "URL contains literal PHP/template code.");
        }

        if (string.IsNullOrWhiteSpace(path) || path == "#")
        {
            return Reject(decision, "url.fragment_only", "Fragment-only or empty path destinations are rejected.");
        }

        if (!string.IsNullOrWhiteSpace(extension) && MediaExtensions.Contains(extension))
        {
            return Reject(decision, "url.media_asset", $"Media/binary extension '{extension}' rejected.");
        }

        if (!string.IsNullOrWhiteSpace(extension) && ScriptExtensions.Contains(extension))
        {
            return Reject(decision, "url.script_asset", $"Script extension '{extension}' rejected.");
        }

        if (LooksLikeBundledAsset(path))
        {
            return Reject(decision, "url.bundled_asset", "Bundled front-end asset path rejected.");
        }

        if (IsCdnHost(host))
        {
            return Reject(decision, "url.cdn_host", "CDN/image host rejected.");
        }

        if (IsLowValueUtilityHost(host) || DomainHealthPolicy.IsParkingOrMarketplaceHost(host))
        {
            return Reject(decision, "url.low_value_host", "Utility, demo, parked, or marketplace host rejected.");
        }

        if (IsGenericRegistryHost(host) && ContainsAny(path, RegistryChromePathTokens))
        {
            return Reject(
                decision,
                "url.registry_chrome",
                "Generic registry infrastructure or legal/marketing page rejected.");
        }

        if (IsAuthenticationHost(host))
        {
            return Reject(decision, "url.auth_host", "SSO/authentication host rejected by default.");
        }

        if (IsAssociationOutboundNoiseHost(host))
        {
            return Reject(
                decision,
                "url.association_outbound_noise",
                "Association outbound partner/event/advocacy host rejected for organization discovery.");
        }

        if (!allowConfiguredApiProvider && ContainsAny(full, ApiPathTokens))
        {
            return Reject(decision, "url.api_endpoint", "API/embed/static endpoint rejected.");
        }

        if (ContainsAny(full, LowValuePathTokens))
        {
            return Reject(decision, "url.low_value_legal", "Privacy, terms, or legal page rejected for investigation.");
        }

        if (ContainsAny(full, TrackingTokens))
        {
            return Reject(decision, "url.tracking", "Tracking or analytics URL rejected.");
        }

        if (ContainsAny(host + path, EmbedHostHints))
        {
            return Reject(decision, "url.embed_host", "Embed/player host rejected.");
        }

        if (MatchesHost(host, SocialHosts))
        {
            return Reject(decision, "url.social_platform", "Social platform host cannot be a target business source.");
        }

        if (MatchesHost(host, AppStoreHosts))
        {
            return Reject(decision, "url.app_store", "App store listing cannot be a target business source.");
        }

        if (host.StartsWith("forum.", StringComparison.Ordinal)
            || host.StartsWith("forums.", StringComparison.Ordinal)
            || ContainsAny(path, ForumPathTokens))
        {
            return Reject(decision, "url.forum", "Forum or member profile page cannot be a target business source.");
        }

        if (IsUnrelatedEducationalHost(host, industries))
        {
            return Reject(decision, "url.educational_unrelated", "Educational host has no evidence of matching the target industries.");
        }

        var geography = EvaluateGeographyRelevance(normalized, countries);
        if (!geography.Accepted)
        {
            decision.PagePurpose = ClassifyPagePurpose(full, path, host);
            return Reject(decision, geography.ReasonCode, geography.Explanation);
        }

        decision.PagePurpose = ClassifyPagePurpose(full, path, host);
        if (decision.PagePurpose == UrlPagePurpose.AssetOrApi)
        {
            return Reject(decision, "url.asset_or_api", "URL classified as asset or API.");
        }

        decision.Accepted = true;
        decision.ReasonCode = "url.accepted";
        decision.Explanation = $"Admitted as {decision.PagePurpose}.";
        return decision;
    }

    public static UrlAdmissionDecision EvaluateGeographyRelevance(
        string? url,
        IReadOnlyList<string>? countries)
    {
        var decision = new UrlAdmissionDecision { Accepted = true, ReasonCode = "url.geo_ok" };
        if (string.IsNullOrWhiteSpace(url) || countries is null || countries.Count == 0)
        {
            decision.Explanation = "No geography filter applied.";
            return decision;
        }

        var normalized = UrlCanonicalizer.Canonicalize(url) ?? url;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return Reject(decision, "url.invalid", "URL could not be parsed for geography checks.");
        }

        var host = uri.Host;
        if (!CountryScopeHelper.HostMatchesCountries(host, countries))
        {
            return Reject(
                decision,
                "url.foreign_host",
                "Host country scope does not match the research geography.");
        }

        if (CountryScopeHelper.PathLooksOutOfCountryScope(uri.AbsolutePath, countries))
        {
            return Reject(
                decision,
                "url.foreign_path",
                "URL path indicates a foreign country scope outside the research geography.");
        }

        decision.Explanation = "Geography relevance accepted.";
        return decision;
    }

    public static UrlPagePurpose ClassifyPagePurpose(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return UrlPagePurpose.Unknown;
        }

        var normalized = UrlCanonicalizer.Canonicalize(url) ?? url;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return UrlPagePurpose.Unknown;
        }

        return ClassifyPagePurpose(normalized.ToLowerInvariant(), uri.AbsolutePath, uri.Host.ToLowerInvariant());
    }

    public static bool IsUsefulPendingPurpose(UrlPagePurpose purpose)
        => purpose is UrlPagePurpose.Contact
            or UrlPagePurpose.About
            or UrlPagePurpose.Team
            or UrlPagePurpose.Location
            or UrlPagePurpose.FranchiseLocation
            or UrlPagePurpose.CompanyProfile
            or UrlPagePurpose.DirectoryListing;

    public static bool IsExpansionRoot(UrlPagePurpose purpose)
        => purpose is UrlPagePurpose.CompanyProfile
            or UrlPagePurpose.Contact
            or UrlPagePurpose.About
            or UrlPagePurpose.Team
            or UrlPagePurpose.Location
            or UrlPagePurpose.FranchiseLocation
            or UrlPagePurpose.DirectoryListing;

    public static bool IsLowValueExpansionPurpose(UrlPagePurpose purpose)
        => purpose is UrlPagePurpose.JobListing
            or UrlPagePurpose.Navigation
            or UrlPagePurpose.Listicle
            or UrlPagePurpose.AssetOrApi;

    public static string? BuildRepetitionFingerprint(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var path = uri.AbsolutePath ?? string.Empty;
        if (!LooksLikeRepetitiveDetailPath(path))
        {
            return null;
        }

        var normalizedPath = System.Text.RegularExpressions.Regex.Replace(
            path.ToLowerInvariant(),
            @"/\d+(/|$)",
            "/{n}$1");
        normalizedPath = System.Text.RegularExpressions.Regex.Replace(
            normalizedPath,
            @"/(page|p|pagina|offset|start)/[^/]+",
            "/$1/{n}");
        return $"{uri.Host.ToLowerInvariant()}{normalizedPath}";
    }

    private static bool LooksLikeRepetitiveDetailPath(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.Contains("/get_details/", StringComparison.Ordinal)
               || lower.Contains("/get-details/", StringComparison.Ordinal)
               || lower.Contains("/details/", StringComparison.Ordinal)
               || lower.Contains("/item/", StringComparison.Ordinal)
               || lower.Contains("/product/", StringComparison.Ordinal)
               || System.Text.RegularExpressions.Regex.IsMatch(lower, @"/page/\d+")
               || System.Text.RegularExpressions.Regex.IsMatch(lower, @"/\d{4,}");
    }

    private static bool HasUnresolvedUndefinedSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lower = value.ToLowerInvariant();
        return lower.Contains("/undefined/", StringComparison.Ordinal)
               || lower.EndsWith("/undefined", StringComparison.Ordinal)
               || lower.Contains("/null/", StringComparison.Ordinal)
               || lower.Contains("%undefined%", StringComparison.Ordinal);
    }

    private static bool ContainsPhpTemplateFragment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("<?", StringComparison.Ordinal)
               || value.Contains("<?=", StringComparison.Ordinal)
               || value.Contains("?>", StringComparison.Ordinal)
               || value.Contains("{$", StringComparison.Ordinal)
               || value.Contains("${", StringComparison.Ordinal)
               || value.Contains("<%", StringComparison.Ordinal)
               || value.Contains("%>", StringComparison.Ordinal);
    }

    private static bool HasUnencodedWhitespace(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static UrlPagePurpose ClassifyPagePurpose(string full, string path, string host)
    {
        if (LooksLikeBundledAsset(path) || IsCdnHost(host) || ContainsAny(full, ApiPathTokens))
        {
            return UrlPagePurpose.AssetOrApi;
        }

        if (ContainsAny(path, JobHints) || ContainsAny(full, JobHints))
        {
            return UrlPagePurpose.JobListing;
        }

        if (ContainsAny(path, ListicleHints) || ContainsAny(full, ListicleHints))
        {
            return UrlPagePurpose.Listicle;
        }

        if (ContainsAny(path, NavigationHints))
        {
            return UrlPagePurpose.Navigation;
        }

        if (IsGenericRegistryHost(host) && ContainsAny(path, RegistryChromePathTokens))
        {
            return UrlPagePurpose.Navigation;
        }

        if (SystemDirectoryCatalog.IsSeedOrDirectoryHost(host)
            && !LooksLikeFranchiseLocation(path)
            && !ContainsAny(path, LowValuePathTokens)
            && !ContainsAny(path, NavigationHints)
            && !ContainsAny(path, RegistryChromePathTokens))
        {
            return UrlPagePurpose.DirectoryListing;
        }

        if (ContainsAny(path, ContactHints))
        {
            return UrlPagePurpose.Contact;
        }

        if (ContainsAny(path, AboutHints))
        {
            return UrlPagePurpose.About;
        }

        if (ContainsAny(path, TeamHints))
        {
            return UrlPagePurpose.Team;
        }

        if (LooksLikeFranchiseLocation(path))
        {
            return UrlPagePurpose.FranchiseLocation;
        }

        if (ContainsAny(path, LocationHints))
        {
            return UrlPagePurpose.Location;
        }

        if (path is "/" or "" || path.Count(c => c == '/') <= 1)
        {
            return UrlPagePurpose.CompanyProfile;
        }

        return UrlPagePurpose.Unknown;
    }

    private static bool LooksLikeFranchiseLocation(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.Contains("/clubs/", StringComparison.Ordinal)
               || lower.Contains("/club/", StringComparison.Ordinal)
               || lower.Contains("/locations/", StringComparison.Ordinal)
               || lower.Contains("/studios/", StringComparison.Ordinal)
               || lower.Contains("/branches/", StringComparison.Ordinal)
               || lower.Contains("/centres/", StringComparison.Ordinal)
               || lower.Contains("/centers/", StringComparison.Ordinal);
    }

    private static bool LooksLikeBundledAsset(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.Contains(".asset-", StringComparison.Ordinal)
            || lower.Contains(".chunk.", StringComparison.Ordinal)
            || lower.Contains("-chunk.", StringComparison.Ordinal)
            || lower.Contains("_payload.json", StringComparison.Ordinal)
            || lower.EndsWith("/_payload.json", StringComparison.Ordinal))
        {
            return true;
        }

        return lower.Contains("/assets/", StringComparison.Ordinal)
               && (lower.EndsWith(".js", StringComparison.Ordinal)
                   || lower.EndsWith(".css", StringComparison.Ordinal)
                   || lower.Contains(".jpg.", StringComparison.Ordinal)
                   || lower.Contains(".png.", StringComparison.Ordinal));
    }

    private static bool IsCdnHost(string host)
    {
        foreach (var hint in CdnHostHints)
        {
            if (host.Equals(hint, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLowValueUtilityHost(string host)
    {
        foreach (var hint in LowValueHostHints)
        {
            if (host.Equals(hint, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGenericRegistryHost(string host)
    {
        foreach (var hint in GenericRegistryHosts)
        {
            if (host.Equals(hint, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAuthenticationHost(string host)
    {
        foreach (var prefix in AuthHostPrefixes)
        {
            if (host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAssociationOutboundNoiseHost(string host)
    {
        foreach (var hint in AssociationOutboundNoiseHosts)
        {
            if (host.Equals(hint, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesHost(string host, IEnumerable<string> hosts)
    {
        foreach (var candidate in hosts)
        {
            if (host.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnrelatedEducationalHost(string host, IReadOnlyList<string>? industries)
    {
        var isEducational = host.EndsWith(".edu", StringComparison.OrdinalIgnoreCase)
                            || host.Contains(".edu.", StringComparison.OrdinalIgnoreCase)
                            || host.StartsWith("university.", StringComparison.OrdinalIgnoreCase);
        if (!isEducational)
        {
            return false;
        }

        if (industries is null || industries.Count == 0)
        {
            return false;
        }

        foreach (var industry in industries)
        {
            var value = industry.ToLowerInvariant();
            if (value.Contains("school", StringComparison.Ordinal)
                || value.Contains("education", StringComparison.Ordinal)
                || value.Contains("university", StringComparison.Ordinal)
                || value.Contains("academy", StringComparison.Ordinal)
                || value.Contains("learning", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHtmlContentType(string contentType)
    {
        var media = contentType.Split(';', 2)[0].Trim();
        return HtmlContentTypes.Contains(media);
    }

    private static string GetExtension(string path)
    {
        var leaf = path;
        var slash = path.LastIndexOf('/');
        if (slash >= 0 && slash < path.Length - 1)
        {
            leaf = path[(slash + 1)..];
        }

        var query = leaf.IndexOf('?');
        if (query >= 0)
        {
            leaf = leaf[..query];
        }

        var dot = leaf.LastIndexOf('.');
        if (dot <= 0 || dot == leaf.Length - 1)
        {
            return string.Empty;
        }

        return leaf[dot..];
    }

    private static bool ContainsAny(string haystack, IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static UrlAdmissionDecision Reject(UrlAdmissionDecision decision, string code, string explanation)
    {
        decision.Accepted = false;
        decision.ReasonCode = code;
        decision.Explanation = explanation;
        decision.PagePurpose = UrlPagePurpose.AssetOrApi;
        return decision;
    }
}
