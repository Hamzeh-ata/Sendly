using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public class ExtractedPageFact
{
    public string FactType { get; set; } = string.Empty;

    public string? FactValue { get; set; }

    public EvidenceConfidenceKind ConfidenceKind { get; set; } = EvidenceConfidenceKind.Verified;

    public string? SourceUrl { get; set; }

    public string? ExtractionMethod { get; set; }
}

public class ExtractedPageData
{
    public string? Title { get; set; }

    public string? CanonicalUrl { get; set; }

    public string? OrganizationName { get; set; }

    public string? OrganizationUrl { get; set; }

    public string? OpenGraphTitle { get; set; }

    public string? OpenGraphSiteName { get; set; }

    public string? PageHeading { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Excerpt { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public List<string> Emails { get; set; } = [];

    public List<string> Phones { get; set; } = [];

    public List<string> WhatsApps { get; set; } = [];

    public bool IsErrorPage { get; set; }

    public bool IsNonHtmlAsset { get; set; }

    public List<string> SameOriginLinks { get; set; } = [];

    public List<string> ExternalLinks { get; set; } = [];

    public List<ExtractedPageFact> Facts { get; set; } = [];

    public bool UsedStructuredData { get; set; }
}

public static class HtmlPageExtractor
{
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PhoneRegex = new(
        @"(\+?\d[\d\s\-()]{7,}\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex HrefRegex = new(
        @"href\s*=\s*[""']([^""'#]+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MailtoRegex = new(
        @"mailto:([^""'\s?>]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TelRegex = new(
        @"tel:([^""'\s?>]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WhatsAppLinkRegex = new(
        @"(?:https?:)?//(?:wa\.me|api\.whatsapp\.com/send)\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AcademicYearRegex = new(
        @"\b(?:19|20)\d{2}\s*[-/]\s*(?:19|20)\d{2}\b",
        RegexOptions.Compiled);

    private static readonly Regex CalendarDateRegex = new(
        @"\b(?:\d{1,2}[-/.]\d{1,2}[-/.](?:19|20)\d{2}|(?:19|20)\d{2}[-/.]\d{1,2}[-/.]\d{1,2})\b",
        RegexOptions.Compiled);

    private static readonly Regex TitleRegex = new(
        @"<title[^>]*>(.*?)</title>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex CanonicalRegex = new(
        @"<link[^>]*rel\s*=\s*[""']canonical[""'][^>]*href\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex MetaDescriptionRegex = new(
        @"<meta[^>]*name\s*=\s*[""']description[""'][^>]*content\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex OpenGraphRegex = new(
        @"<meta[^>]*property\s*=\s*[""'](og:[^""']+)[""'][^>]*content\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex JsonLdRegex = new(
        @"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex HeadingRegex = new(
        @"<h1[^>]*>(.*?)</h1>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly string[] PriorityPathHints =
    [
        "contact", "about", "team", "people", "support", "services", "locations", "company"
    ];

    private static readonly HashSet<string> OrganizationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Organization", "LocalBusiness", "Corporation", "School", "EducationalOrganization",
        "Store", "Restaurant", "SportsActivityLocation", "HealthAndBeautyBusiness",
        "MedicalBusiness", "ProfessionalService", "HomeAndConstructionBusiness"
    };

    public static ExtractedPageData Extract(string html, Uri pageUrl)
    {
        if (IsNonHtmlAssetUrl(pageUrl) || LooksLikeCssOrScript(html, pageUrl))
        {
            return new ExtractedPageData
            {
                IsNonHtmlAsset = true,
                Title = pageUrl.AbsolutePath,
                ContentHash = ComputeHash(html ?? string.Empty)
            };
        }

        var text = HtmlToText(html);
        var isErrorPage = LooksLikeErrorPage(html, pageUrl);
        var contentHtml = StripNonContent(html);
        var emails = isErrorPage ? [] : ExtractEmails(contentHtml, html);
        var phones = isErrorPage ? [] : ExtractPhones(contentHtml, text);
        var whatsApps = isErrorPage ? [] : ExtractWhatsApps(html, text);
        var title = ExtractTitle(html);
        var heading = ExtractHeading(html);
        var canonical = ExtractCanonical(html, pageUrl);
        var description = ExtractMetaDescription(html);
        var openGraph = ExtractOpenGraph(html);
        var structured = ExtractJsonLdOrganization(html, pageUrl);
        var links = DiscoverLinks(html, pageUrl);
        var hash = ComputeHash(text);

        if (!isErrorPage
            && !string.IsNullOrWhiteSpace(structured.Email)
            && IsLikelyEmail(structured.Email)
            && !emails.Contains(structured.Email, StringComparer.OrdinalIgnoreCase))
        {
            emails.Insert(0, structured.Email);
        }

        if (!isErrorPage
            && !string.IsNullOrWhiteSpace(structured.Phone)
            && IsLikelyPhone(structured.Phone)
            && !phones.Contains(structured.Phone, StringComparer.OrdinalIgnoreCase))
        {
            phones.Insert(0, structured.Phone);
        }

        var data = new ExtractedPageData
        {
            Title = title,
            CanonicalUrl = canonical,
            OrganizationName = isErrorPage ? null : structured.Name,
            OrganizationUrl = structured.Url,
            OpenGraphTitle = openGraph.Title,
            OpenGraphSiteName = openGraph.SiteName,
            IsErrorPage = isErrorPage,
            PageHeading = heading,
            Text = text,
            Excerpt = BuildExcerpt(text),
            ContentHash = hash,
            Emails = emails,
            Phones = phones,
            WhatsApps = whatsApps,
            SameOriginLinks = links.SameOrigin,
            ExternalLinks = links.External,
            UsedStructuredData = !string.IsNullOrWhiteSpace(structured.Name)
        };

        AddFact(data, "pageTitle", title, EvidenceConfidenceKind.Verified, pageUrl.ToString(), "html.title");
        AddFact(data, "pageHeading", heading, EvidenceConfidenceKind.Verified, pageUrl.ToString(), "html.h1");
        AddFact(data, "canonicalUrl", canonical, EvidenceConfidenceKind.Verified, pageUrl.ToString(), "html.canonical");
        AddFact(data, "metaDescription", description, EvidenceConfidenceKind.Verified, pageUrl.ToString(), "html.meta");
        AddFact(data, "ogTitle", openGraph.Title, EvidenceConfidenceKind.Verified, pageUrl.ToString(), "html.open_graph");
        AddFact(data, "ogSiteName", openGraph.SiteName, EvidenceConfidenceKind.Verified, pageUrl.ToString(), "html.open_graph");
        AddFact(data, "organizationName", structured.Name, EvidenceConfidenceKind.Verified, pageUrl.ToString(), "json_ld.organization");
        AddFact(data, "organizationUrl", structured.Url, EvidenceConfidenceKind.Verified, pageUrl.ToString(), "json_ld.organization");
        foreach (var email in emails)
        {
            var method = string.Equals(email, structured.Email, StringComparison.OrdinalIgnoreCase)
                ? "json_ld.email"
                : "html.email";
            AddFact(data, "email", email, EvidenceConfidenceKind.Unverified, pageUrl.ToString(), method);
        }

        foreach (var phone in phones)
        {
            var method = string.Equals(phone, structured.Phone, StringComparison.OrdinalIgnoreCase)
                ? "json_ld.telephone"
                : "html.phone";
            AddFact(data, "phone", phone, EvidenceConfidenceKind.Unverified, pageUrl.ToString(), method);
        }

        foreach (var whatsApp in whatsApps)
        {
            AddFact(data, "whatsapp", whatsApp, EvidenceConfidenceKind.Unverified, pageUrl.ToString(), "html.whatsapp");
        }

        return data;
    }

    public static IEnumerable<string> OrderPriorityLinks(IEnumerable<string> links)
    {
        return links
            .OrderByDescending(IsPriorityPath)
            .ThenBy(x => x.Length);
    }

    public static bool IsPriorityPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.ToLowerInvariant();
        foreach (var hint in PriorityPathHints)
        {
            if (path.Contains(hint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddFact(
        ExtractedPageData data,
        string type,
        string? value,
        EvidenceConfidenceKind kind,
        string sourceUrl,
        string? extractionMethod = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        data.Facts.Add(new ExtractedPageFact
        {
            FactType = type,
            FactValue = value.Trim(),
            ConfidenceKind = kind,
            SourceUrl = sourceUrl,
            ExtractionMethod = extractionMethod
        });
    }

    private static (string? Title, string? SiteName) ExtractOpenGraph(string html)
    {
        string? title = null;
        string? siteName = null;
        foreach (Match match in OpenGraphRegex.Matches(html))
        {
            var property = match.Groups[1].Value.Trim();
            var content = WebUtility.HtmlDecode(match.Groups[2].Value).Trim();
            if (property.Equals("og:title", StringComparison.OrdinalIgnoreCase))
            {
                title = content;
            }
            else if (property.Equals("og:site_name", StringComparison.OrdinalIgnoreCase))
            {
                siteName = content;
            }
        }

        return (title, siteName);
    }

    private static (string? Name, string? Url, string? Email, string? Phone) ExtractJsonLdOrganization(
        string html,
        Uri pageUrl)
    {
        foreach (Match match in JsonLdRegex.Matches(html))
        {
            var json = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var found = FindOrganization(doc.RootElement, pageUrl);
                if (!string.IsNullOrWhiteSpace(found.Name))
                {
                    return found;
                }
            }
            catch (JsonException)
            {
            }
        }

        return (null, null, null, null);
    }

    private static (string? Name, string? Url, string? Email, string? Phone) FindOrganization(
        JsonElement element,
        Uri pageUrl)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindOrganization(item, pageUrl);
                if (!string.IsNullOrWhiteSpace(found.Name))
                {
                    return found;
                }
            }

            return (null, null, null, null);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, null, null, null);
        }

        if (element.TryGetProperty("@graph", out var graph))
        {
            return FindOrganization(graph, pageUrl);
        }

        if (!IsOrganizationType(element))
        {
            return (null, null, null, null);
        }

        var name = ReadString(element, "name");
        var url = ReadString(element, "url") ?? pageUrl.ToString();
        var email = ReadString(element, "email");
        var phone = ReadString(element, "telephone");
        return (name, url, email, phone);
    }

    private static bool IsOrganizationType(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var typeElement))
        {
            return false;
        }

        if (typeElement.ValueKind == JsonValueKind.String)
        {
            return OrganizationTypes.Contains(typeElement.GetString() ?? string.Empty);
        }

        if (typeElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in typeElement.EnumerateArray())
            {
                if (OrganizationTypes.Contains(item.GetString() ?? string.Empty))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        return null;
    }

    private static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutScripts = Regex.Replace(
            html,
            @"<script[\s\S]*?</script>|<style[\s\S]*?</style>",
            " ",
            RegexOptions.IgnoreCase);
        var text = TagRegex.Replace(withoutScripts, " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static List<string> ExtractEmails(string contentHtml, string originalHtml)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MailtoRegex.Matches(originalHtml))
        {
            var email = WebUtility.UrlDecode(match.Groups[1].Value).Split('?')[0].Trim();
            if (IsLikelyEmail(email) && seen.Add(email))
            {
                result.Add(email);
            }
        }

        foreach (Match match in EmailRegex.Matches(contentHtml))
        {
            var email = match.Value.Trim();
            if (IsLikelyEmail(email) && seen.Add(email))
            {
                result.Add(email);
            }
        }

        return result;
    }

    private static List<string> ExtractPhones(string html, string text)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in TelRegex.Matches(html))
        {
            var phone = WebUtility.UrlDecode(match.Groups[1].Value).Trim();
            if (IsLikelyPhone(phone) && seen.Add(phone))
            {
                result.Add(phone);
            }
        }

        foreach (Match match in PhoneRegex.Matches(text))
        {
            var phone = match.Value.Trim();
            if (IsLikelyPhone(phone) && seen.Add(phone))
            {
                result.Add(phone);
            }
        }

        return result;
    }

    private static List<string> ExtractWhatsApps(string html, string text)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in WhatsAppLinkRegex.Matches(html))
        {
            var digits = new string(match.Value.Where(char.IsDigit).ToArray());
            if (digits.Length >= 8 && digits.Length <= 15 && seen.Add(digits))
            {
                result.Add("+" + digits.TrimStart('0'));
            }
        }

        if (!text.Contains("whatsapp", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("whatsapp", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        return result;
    }

    private static string? ExtractTitle(string html)
    {
        var match = TitleRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        return WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
    }

    private static string? ExtractHeading(string html)
    {
        var match = HeadingRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        var raw = TagRegex.Replace(match.Groups[1].Value, " ");
        var text = WebUtility.HtmlDecode(raw);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? ExtractCanonical(string html, Uri pageUrl)
    {
        var match = CanonicalRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value.Trim();
        if (Uri.TryCreate(pageUrl, value, out var uri))
        {
            return uri.ToString();
        }

        return value;
    }

    private static string? ExtractMetaDescription(string html)
    {
        var match = MetaDescriptionRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        return WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
    }

    private static (List<string> SameOrigin, List<string> External) DiscoverLinks(string html, Uri pageUrl)
    {
        var sameOrigin = new List<string>();
        var external = new List<string>();
        var seenSame = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenExternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in HrefRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            if (string.IsNullOrWhiteSpace(href)
                || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(pageUrl, href, out var absolute))
            {
                continue;
            }

            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            var key = absolute.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (string.Equals(absolute.Host, pageUrl.Host, StringComparison.OrdinalIgnoreCase))
            {
                if (seenSame.Add(key))
                {
                    sameOrigin.Add(absolute.ToString());
                }
            }
            else if (seenExternal.Add(key))
            {
                external.Add(absolute.ToString());
            }
        }

        return (sameOrigin, external);
    }

    private static string BuildExcerpt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Length <= 4000 ? text : text[..4000];
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(bytes);
    }

    private static readonly string[] ImageLikeEmailSuffixes =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp", ".ico", ".tif", ".tiff",
        ".avif", ".heic", ".woff", ".woff2", ".css", ".js", ".map", ".pdf"
    ];

    private static bool IsNonHtmlAssetUrl(Uri pageUrl)
    {
        var path = pageUrl.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".css", StringComparison.Ordinal)
               || path.EndsWith(".js", StringComparison.Ordinal)
               || path.EndsWith(".mjs", StringComparison.Ordinal)
               || path.EndsWith(".ico", StringComparison.Ordinal)
               || path.EndsWith(".map", StringComparison.Ordinal)
               || path.EndsWith(".woff", StringComparison.Ordinal)
               || path.EndsWith(".woff2", StringComparison.Ordinal)
               || path.Contains("favicon", StringComparison.Ordinal);
    }

    private static bool LooksLikeCssOrScript(string html, Uri pageUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        var trimmed = html.TrimStart();
        if (trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            return false;
        }

        return pageUrl.AbsolutePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
               || pageUrl.AbsolutePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
               || (trimmed.Contains('{', StringComparison.Ordinal)
                   && trimmed.Contains('}', StringComparison.Ordinal)
                   && !trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase)
                   && !trimmed.Contains("<body", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeErrorPage(string html, Uri pageUrl)
    {
        var title = (ExtractTitle(html) ?? string.Empty).ToLowerInvariant();
        var path = pageUrl.AbsolutePath.ToLowerInvariant();
        return title.Contains("404", StringComparison.Ordinal)
               || title.Contains("not found", StringComparison.Ordinal)
               || title.Contains("page not found", StringComparison.Ordinal)
               || title.Contains("access denied", StringComparison.Ordinal)
               || title.Contains("403 forbidden", StringComparison.Ordinal)
               || title.Contains("500 internal", StringComparison.Ordinal)
               || path.Contains("/404", StringComparison.Ordinal)
               || path.Contains("/error", StringComparison.Ordinal);
    }

    private static string StripNonContent(string html)
    {
        var withoutScripts = Regex.Replace(
            html,
            @"<script[\s\S]*?</script>",
            " ",
            RegexOptions.IgnoreCase);
        withoutScripts = Regex.Replace(
            withoutScripts,
            @"<style[\s\S]*?</style>",
            " ",
            RegexOptions.IgnoreCase);
        withoutScripts = Regex.Replace(
            withoutScripts,
            @"<nav[\s\S]*?</nav>",
            " ",
            RegexOptions.IgnoreCase);
        withoutScripts = Regex.Replace(
            withoutScripts,
            @"<header[\s\S]*?</header>",
            " ",
            RegexOptions.IgnoreCase);
        withoutScripts = Regex.Replace(
            withoutScripts,
            @"<footer[\s\S]*?</footer>",
            " ",
            RegexOptions.IgnoreCase);
        return withoutScripts;
    }

    public static bool IsLikelyPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        var value = phone.Trim();
        if (AcademicYearRegex.IsMatch(value) || CalendarDateRegex.IsMatch(value))
        {
            return false;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length < 8 || digits.Length > 15)
        {
            return false;
        }

        if (digits.StartsWith("20", StringComparison.Ordinal) && digits.Length == 8)
        {
            return false;
        }

        if (digits.Length == 4)
        {
            return false;
        }

        if (digits.Length == 8
            && Regex.IsMatch(digits, @"^(19|20)\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])$"))
        {
            return false;
        }

        if (digits.Distinct().Count() == 1)
        {
            return false;
        }

        if (value.Contains('.', StringComparison.Ordinal) && !value.Contains('+', StringComparison.Ordinal))
        {
            return false;
        }

        if (value.Contains("AED", StringComparison.OrdinalIgnoreCase)
            || value.Contains("USD", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith('$')
            || value.Contains("ID", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsLikelyEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return false;
        }

        var lower = email.ToLowerInvariant().Trim().TrimEnd('.');
        foreach (var suffix in ImageLikeEmailSuffixes)
        {
            if (lower.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        var at = lower.IndexOf('@');
        if (at <= 0 || at >= lower.Length - 3)
        {
            return false;
        }

        var domain = lower[(at + 1)..];
        if (domain.Contains('@', StringComparison.Ordinal)
            || domain.StartsWith('!')
            || domain.Contains("2x.", StringComparison.Ordinal)
            || domain.Contains("3x.", StringComparison.Ordinal)
            || !domain.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        return email.Length <= 320 && EmailAddressParser.IsValidEmail(lower);
    }
}
