using System.Net;
using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;
using Sendly.Domain.Services;

namespace Sendly.Application.Services.WebsiteInvestigation;

public static class DirectoryListingExtractor
{
    private static readonly Regex BlockRegex = new(
        @"<(article|li|tr|section|div)([^>]*(?:class|id)\s*=\s*[""'][^""']*(?:card|listing|item|school|company|result|profile|org|institution)[^""']*[""'][^>]*)>(.*?)</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex HeadingRegex = new(
        @"<h[1-4][^>]*>(.*?)</h[1-4]>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex AnchorRegex = new(
        @"<a[^>]*href\s*=\s*[""']([^""'#]+)[""'][^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex MailtoRegex = new(
        @"mailto:([^""'\s?>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TelRegex = new(
        @"tel:([^""'\s?>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhatsAppRegex = new(
        @"wa\.me/(\d+)|whatsapp\.com/send\?phone=(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PhoneRegex = new(
        @"(\+?\d[\d\s\-()]{7,}\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "contact", "contact us", "about", "about us", "welcome", "directory",
        "read more", "learn more", "details", "view", "more", "next", "previous",
        "features", "how it works", "admissions", "admission", "tuition fees", "tuition",
        "register online", "register", "login", "login now", "sign in", "career", "careers",
        "our team", "our location", "our campus", "welcome message", "news & updates",
        "calendar", "academic calendar", "mission", "arabic", "english", "photo gallery & video",
        "membership", "board of directors", "staff", "committees", "events", "store",
        "search for:", "search for", "company search", "renew membership", "digital badging",
        "my account", "partners", "charities", "about bscai", "member resource center",
        "industry research", "education & events", "global trade shows", "lifetime benefits",
        "ways to give", "case studies", "getting started", "business discovery",
        "data management", "investigations", "entity verification", "financial services",
        "third-party risk management", "legal-entity data principles", "ngos",
        "get direction", "get directions", "directions", "preschools", "join nais",
        "jobs at nais", "legal and privacy information", "pricing", "api and bulk pricing",
        "programs", "work with us", "meet the team", "leadership", "journalism",
        "professional services", "use cases", "resources", "team partners careers",
        "legal news and tips", "employer of national service", "civic fellowship",
        "lasting impressions", "close up in the news", "middle school programs",
        "purpose, history and principles", "events and insights", "api developer documentation",
        "الرئيسية", "تواصل", "اتصل بنا", "من نحن", "المزيد", "التفاصيل",
        "تسجيل الدخول", "البحث عن مدرسة", "المزايا", "القسائم التعليمية", "عن مدارس",
        "كيف تعمل مدارس", "العربية", "مدارس أعمال", "سجل الان تواصل معنا"
    };

    public static List<ListingOrganization> Extract(string html, Uri pageUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var listings = new List<ListingOrganization>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in BlockRegex.Matches(html))
        {
            var blockHtml = match.Groups[3].Value;
            var listing = ParseBlock(blockHtml, pageUrl, "listing.semantic_block");
            if (listing is null || !TryAdd(listings, seenKeys, listing))
            {
                continue;
            }
        }

        if (listings.Count == 0)
        {
            ExtractFromProfileAnchors(html, pageUrl, listings, seenKeys);
        }

        return listings;
    }

    private static void ExtractFromProfileAnchors(
        string html,
        Uri pageUrl,
        List<ListingOrganization> listings,
        HashSet<string> seenKeys)
    {
        foreach (Match match in AnchorRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            var label = NormalizeText(match.Groups[2].Value);
            if (string.IsNullOrWhiteSpace(label) || IsGenericName(label) || label.Length < 3)
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

            var sameHost = string.Equals(absolute.Host, pageUrl.Host, StringComparison.OrdinalIgnoreCase);
            var pathDepth = absolute.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
            if (sameHost && pathDepth < 2)
            {
                continue;
            }

            var external = sameHost ? null : absolute.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (!sameHost && !IsPlausibleOfficialWebsite(external, pageUrl))
            {
                continue;
            }

            var listing = new ListingOrganization
            {
                Name = TrimName(label),
                ProfileUrl = sameHost ? absolute.ToString() : null,
                OfficialWebsiteUrl = external,
                SourcePageUrl = pageUrl.ToString(),
                ExtractionMethod = "listing.profile_anchor",
                Confidence = sameHost ? 58 : 62
            };

            TryAdd(listings, seenKeys, listing);
            if (listings.Count >= 40)
            {
                return;
            }
        }
    }

    private static ListingOrganization? ParseBlock(string blockHtml, Uri pageUrl, string method)
    {
        var text = NormalizeText(blockHtml);
        if (string.IsNullOrWhiteSpace(text) || text.Length < 4)
        {
            return null;
        }

        var name = ExtractName(blockHtml, text);
        if (string.IsNullOrWhiteSpace(name) || IsGenericName(name))
        {
            return null;
        }

        var profileUrl = FindPreferredSameOriginLink(blockHtml, pageUrl);
        var officialWebsite = FindPreferredExternalLink(blockHtml, pageUrl);
        if (!IsPlausibleOfficialWebsite(officialWebsite, pageUrl))
        {
            officialWebsite = null;
        }

        var emailOriginal = FindEmail(blockHtml, text);
        var phoneOriginal = FindPhone(blockHtml, text);
        var whatsApp = FindWhatsApp(blockHtml);

        if (IsFormLabelFalsePositive(emailOriginal, text))
        {
            emailOriginal = null;
        }

        if (IsFormLabelFalsePositive(phoneOriginal, text))
        {
            phoneOriginal = null;
        }

        return new ListingOrganization
        {
            Name = name,
            ProfileUrl = profileUrl,
            OfficialWebsiteUrl = officialWebsite,
            Email = NormalizeEmail(emailOriginal),
            EmailOriginal = emailOriginal,
            Phone = NormalizePhone(phoneOriginal),
            PhoneOriginal = phoneOriginal,
            WhatsApp = whatsApp,
            Address = FindAddress(text),
            City = FindCity(text),
            SourcePageUrl = pageUrl.ToString(),
            ExtractionMethod = method,
            Confidence = ComputeConfidence(name, profileUrl, officialWebsite, emailOriginal, phoneOriginal)
        };
    }

    private static string? FindAddress(string text)
    {
        var match = Regex.Match(
            text,
            @"\b\d{1,6}\s+[A-Za-z0-9 .,'-]{3,60}\b(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Drive|Dr|Lane|Ln|Way|Court|Ct)\b[^\n|]{0,40}",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Value.Trim();
        return value.Length > 160 ? value[..160] : value;
    }

    private static string? FindCity(string text)
    {
        var match = Regex.Match(
            text,
            @"\b([A-Z][a-zA-Z .'-]{1,40}),\s*([A-Z]{2})\s+\d{5}(?:-\d{4})?\b");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractName(string blockHtml, string text)
    {
        var heading = HeadingRegex.Match(blockHtml);
        if (heading.Success)
        {
            var headingText = NormalizeText(heading.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(headingText) && !IsGenericName(headingText))
            {
                return TrimName(headingText);
            }
        }

        foreach (Match anchor in AnchorRegex.Matches(blockHtml))
        {
            var label = NormalizeText(anchor.Groups[2].Value);
            if (!string.IsNullOrWhiteSpace(label) && !IsGenericName(label) && label.Length >= 3)
            {
                return TrimName(label);
            }
        }

        var firstLine = text.Split(['\n', '|', '•'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault(x => x.Length >= 3 && !IsGenericName(x));
        return firstLine is null ? null : TrimName(firstLine);
    }

    private static string? FindPreferredSameOriginLink(string blockHtml, Uri pageUrl)
    {
        string? best = null;
        foreach (Match match in AnchorRegex.Matches(blockHtml))
        {
            var href = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(pageUrl, href, out var absolute))
            {
                continue;
            }

            if (!string.Equals(absolute.Host, pageUrl.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (absolute.AbsolutePath is "/" or "")
            {
                continue;
            }

            best = absolute.ToString();
            var label = NormalizeText(match.Groups[2].Value);
            if (!IsGenericName(label) && label.Length >= 3)
            {
                return best;
            }
        }

        return best;
    }

    private static string? FindPreferredExternalLink(string blockHtml, Uri pageUrl)
    {
        foreach (Match match in AnchorRegex.Matches(blockHtml))
        {
            var href = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (!Uri.TryCreate(pageUrl, href, out var absolute))
            {
                continue;
            }

            if (string.Equals(absolute.Host, pageUrl.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            if (IsSocialOrNoiseHost(absolute.Host))
            {
                continue;
            }

            return absolute.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        return null;
    }

    private static string? FindEmail(string blockHtml, string text)
    {
        var mailto = MailtoRegex.Match(blockHtml);
        if (mailto.Success)
        {
            return WebUtility.UrlDecode(mailto.Groups[1].Value.Split('?')[0].Trim());
        }

        var match = EmailRegex.Match(text);
        return match.Success ? match.Value.Trim() : null;
    }

    private static string? FindPhone(string blockHtml, string text)
    {
        var tel = TelRegex.Match(blockHtml);
        if (tel.Success)
        {
            return WebUtility.UrlDecode(tel.Groups[1].Value.Trim());
        }

        var match = PhoneRegex.Match(text);
        return match.Success ? match.Value.Trim() : null;
    }

    private static string? FindWhatsApp(string blockHtml)
    {
        var match = WhatsAppRegex.Match(blockHtml);
        if (!match.Success)
        {
            return null;
        }

        var number = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return string.IsNullOrWhiteSpace(number) ? null : "+" + number.TrimStart('+');
    }

    private static bool TryAdd(
        List<ListingOrganization> listings,
        HashSet<string> seenKeys,
        ListingOrganization listing)
    {
        if (ProspectJunkClassifier.IsJunkOrganization(
                listing.Name,
                listing.ProfileUrl ?? listing.SourcePageUrl))
        {
            return false;
        }

        ApplyGeographyEvidence(listing);
        var key = BuildKey(listing);
        if (!seenKeys.Add(key))
        {
            return false;
        }

        listings.Add(listing);
        return true;
    }

    private static void ApplyGeographyEvidence(ListingOrganization listing)
    {
        if (!string.IsNullOrWhiteSpace(listing.Country))
        {
            return;
        }

        var country = OrganizationCountryGate.InferCountryFromUrl(listing.ProfileUrl)
                      ?? OrganizationCountryGate.InferCountryFromUrl(listing.SourcePageUrl)
                      ?? OrganizationCountryGate.InferCountryFromUrl(listing.OfficialWebsiteUrl);
        if (string.IsNullOrWhiteSpace(country))
        {
            return;
        }

        listing.Country = country;
        listing.CountryEvidenceSource = "listing_url_geography";
    }

    private static string BuildKey(ListingOrganization listing)
    {
        var website = listing.OfficialWebsiteUrl ?? listing.ProfileUrl ?? string.Empty;
        return (listing.Name.Trim().ToLowerInvariant() + "|" + website.ToLowerInvariant());
    }

    private static bool IsGenericName(string name)
    {
        var trimmed = name.Trim();
        if (GenericNames.Contains(trimmed))
        {
            return true;
        }

        if (LooksLikeNavigationOrChromeLabel(trimmed))
        {
            return true;
        }

        return ProspectJunkClassifier.IsJunkOrganization(trimmed);
    }

    private static bool LooksLikeNavigationOrChromeLabel(string name)
    {
        var lower = name.ToLowerInvariant();
        if (LooksLikePhoneOrEmailAsName(name)
            || LooksLikeBrokenMarkupName(name)
            || lower.StartsWith("visit ", StringComparison.Ordinal)
            || lower.StartsWith("join ", StringComparison.Ordinal)
            || lower.StartsWith("get ", StringComparison.Ordinal)
            || lower.StartsWith("why choose", StringComparison.Ordinal)
            || lower.StartsWith("how to ", StringComparison.Ordinal)
            || lower.StartsWith("how it ", StringComparison.Ordinal)
            || lower.StartsWith("what ", StringComparison.Ordinal)
            || lower.StartsWith("where ", StringComparison.Ordinal)
            || lower.StartsWith("does ", StringComparison.Ordinal)
            || lower.StartsWith("is ", StringComparison.Ordinal)
            || lower.StartsWith("request for", StringComparison.Ordinal)
            || (lower.StartsWith("about ", StringComparison.Ordinal) && lower.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3)
            || lower.EndsWith(" membership", StringComparison.Ordinal)
            || lower.Contains("login", StringComparison.Ordinal)
            || lower.Contains("sign in", StringComparison.Ordinal)
            || lower.Contains("password", StringComparison.Ordinal)
            || lower.Contains("schoology", StringComparison.Ordinal)
            || lower.Contains("powerschool", StringComparison.Ordinal)
            || lower.Contains("ms teams", StringComparison.Ordinal)
            || lower.Contains("tuition", StringComparison.Ordinal)
            || lower.Contains("register", StringComparison.Ordinal)
            || lower.Contains("admission", StringComparison.Ordinal)
            || lower.Contains("gallery", StringComparison.Ordinal)
            || lower.Contains("awards", StringComparison.Ordinal)
            || lower.Contains("vision", StringComparison.Ordinal)
            || lower.Contains("mission", StringComparison.Ordinal)
            || lower.Contains("core values", StringComparison.Ordinal)
            || lower.Contains("campus and", StringComparison.Ordinal)
            || lower.Contains("email protected", StringComparison.Ordinal)
            || lower.Contains("[email", StringComparison.Ordinal)
            || lower.Contains("membership", StringComparison.Ordinal)
            || lower.Contains("board of directors", StringComparison.Ordinal)
            || lower.Contains("digital badging", StringComparison.Ordinal)
            || lower.Contains("member resource", StringComparison.Ordinal)
            || lower.Contains("trade show", StringComparison.Ordinal)
            || lower.Contains("my account", StringComparison.Ordinal)
            || lower.Contains("company search", StringComparison.Ordinal)
            || lower.Contains("academic calendar", StringComparison.Ordinal)
            || lower.Contains("get direction", StringComparison.Ordinal)
            || lower.Contains("bulk pricing", StringComparison.Ordinal)
            || lower.Contains("api and", StringComparison.Ordinal)
            || lower.Contains("websocket", StringComparison.Ordinal)
            || lower.Contains("your connection is not", StringComparison.Ordinal)
            || lower.Contains("privacy information", StringComparison.Ordinal)
            || lower.Contains("legal news", StringComparison.Ordinal)
            || lower.Contains("jobs at ", StringComparison.Ordinal)
            || lower.Contains("careers", StringComparison.Ordinal)
            || lower.Contains("resources by role", StringComparison.Ordinal)
            || lower.Contains("getting started", StringComparison.Ordinal)
            || lower.Contains("governance", StringComparison.Ordinal)
            || lower.Contains("partnership program", StringComparison.Ordinal)
            || lower.Contains("use cases", StringComparison.Ordinal)
            || lower.Contains("entity verification", StringComparison.Ordinal)
            || lower.Contains("قسائم", StringComparison.Ordinal)
            || lower.Contains("تسجيل", StringComparison.Ordinal)
            || lower.Contains("دليل", StringComparison.Ordinal)
            || lower.Contains("البحث", StringComparison.Ordinal)
            || Regex.IsMatch(name, @"^(january|february|march|april|may|june|july|august|september|october|november|december)\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(name, @"\b20\d{2}\b"))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeBrokenMarkupName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Contains("alt=", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith('"')
               || trimmed.Contains("\" >", StringComparison.Ordinal)
               || trimmed.Contains("<img", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains("src=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePhoneOrEmailAsName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains('@'))
        {
            return true;
        }

        var digits = Regex.Replace(trimmed, @"[^\d]", string.Empty);
        return digits.Length >= 8 && digits.Length >= trimmed.Count(char.IsLetterOrDigit) - 2;
    }

    private static bool IsPlausibleOfficialWebsite(string? url, Uri pageUrl)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return false;
        }

        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (SystemDirectoryCatalog.IsSeedOrDirectoryHost(absolute.Host)
            || UrlCanonicalizer.AreSameRegistrableDomain(pageUrl.Host, absolute.Host))
        {
            return false;
        }

        if (IsSocialOrNoiseHost(absolute.Host)
            || absolute.Host.Contains("office.com", StringComparison.OrdinalIgnoreCase)
            || absolute.Host.Contains("youtu", StringComparison.OrdinalIgnoreCase)
            || absolute.Host.Contains("schoology.", StringComparison.OrdinalIgnoreCase)
            || absolute.Host.Contains("powerschool.", StringComparison.OrdinalIgnoreCase)
            || absolute.Host.Contains("google.com", StringComparison.OrdinalIgnoreCase)
            || absolute.Host.Contains("maps.google", StringComparison.OrdinalIgnoreCase)
            || absolute.Host.Contains("goo.gl", StringComparison.OrdinalIgnoreCase)
            || absolute.Host.Contains("votervoice.net", StringComparison.OrdinalIgnoreCase)
            || absolute.Host.Contains("closeup.org", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = absolute.AbsolutePath.ToLowerInvariant();
        if (path.EndsWith(".pdf", StringComparison.Ordinal)
            || path.Contains("/auth/", StringComparison.Ordinal)
            || path.Contains("/login", StringComparison.Ordinal)
            || path.Contains("/assets/", StringComparison.Ordinal)
            || path.Contains("school-search", StringComparison.Ordinal)
            || path.Contains("create-account", StringComparison.Ordinal)
            || path.Contains("redirect-register", StringComparison.Ordinal)
            || path.Contains("/maps/", StringComparison.Ordinal)
            || path.Contains("/maps?", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static string TrimName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length > 120)
        {
            trimmed = trimmed[..120].Trim();
        }

        return trimmed;
    }

    private static string NormalizeText(string value)
    {
        var noTags = TagRegex.Replace(value ?? string.Empty, " ");
        var decoded = WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalized = email.Trim().TrimEnd('.').ToLowerInvariant();
        return normalized;
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = Regex.Replace(phone, @"[^\d+]", string.Empty);
        return string.IsNullOrWhiteSpace(digits) ? null : digits;
    }

    private static bool IsFormLabelFalsePositive(string? value, string text)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lower = text.ToLowerInvariant();
        if (value.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        return lower.Contains("type=\"email\"", StringComparison.Ordinal)
               || lower.Contains("name=\"email\"", StringComparison.Ordinal)
               || lower.Contains("placeholder=\"email\"", StringComparison.Ordinal)
               || lower.Contains("type=\"tel\"", StringComparison.Ordinal);
    }

    private static bool IsSocialOrNoiseHost(string host)
    {
        var lower = host.ToLowerInvariant();
        return lower.Contains("facebook.", StringComparison.Ordinal)
               || lower.Contains("instagram.", StringComparison.Ordinal)
               || lower.Contains("twitter.", StringComparison.Ordinal)
               || lower.Contains("linkedin.", StringComparison.Ordinal)
               || lower.Contains("youtube.", StringComparison.Ordinal)
               || lower.Contains("wa.me", StringComparison.Ordinal)
               || lower.Contains("whatsapp.", StringComparison.Ordinal);
    }

    private static int ComputeConfidence(
        string name,
        string? profileUrl,
        string? officialWebsite,
        string? email,
        string? phone)
    {
        var score = 50;
        if (!string.IsNullOrWhiteSpace(profileUrl))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(officialWebsite))
        {
            score += 15;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            score += 5;
        }

        if (name.Length >= 8)
        {
            score += 5;
        }

        return Math.Min(score, 95);
    }
}
