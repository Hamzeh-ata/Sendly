using System.Text.RegularExpressions;

namespace Sendly.Application.Services.MapResearch;

public static class MapContactWebsiteGuard
{
    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "youtu.be",
        "wikipedia.org",
        "ar.wikipedia.org",
        "en.wikipedia.org",
        "facebook.com",
        "www.facebook.com",
        "instagram.com",
        "www.instagram.com",
        "twitter.com",
        "x.com",
        "linkedin.com",
        "www.linkedin.com",
        "tiktok.com",
        "www.tiktok.com",
        "reddit.com",
        "google.com",
        "www.google.com",
        "search.google",
        "bing.com",
        "duckduckgo.com",
        "true-gaming.net",
        "www.true-gaming.net",
        "sqorebda3.com",
        "www.sqorebda3.com",
        "aldiwan.net",
        "www.aldiwan.net",
        "rqeeqa.com",
        "naseehamath.com",
        "uwallet.jo",
        "whatsapp.com",
        "web.whatsapp.com",
        "api.whatsapp.com",
        "telegram.org",
        "t.me",
        "chatgpt.com",
        "chat.openai.com",
        "openai.com",
        "zhihu.com",
        "www.zhihu.com",
        "islamqa.info",
        "arabic.chat",
        "www.arabic.chat",
        "hm.com",
        "sa.hm.com",
        "gemini.google",
        "chrome.google.com"
    };

    private static readonly Regex YearLikePhoneRegex = new(
        @"^(?:19|20)\d{2}\b",
        RegexOptions.Compiled);

    public static bool IsPlausibleWebsite(string? website, string? companyName)
    {
        if (string.IsNullOrWhiteSpace(website))
        {
            return false;
        }

        if (!Uri.TryCreate(website, UriKind.Absolute, out var uri)
            && !Uri.TryCreate("https://" + website.Trim(), UriKind.Absolute, out uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host) || host.Length < 4)
        {
            return false;
        }

        if (BlockedHosts.Contains(host))
        {
            return false;
        }

        foreach (var blocked in BlockedHosts)
        {
            if (host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (host.Contains("wikipedia.", StringComparison.OrdinalIgnoreCase)
            || host.Contains("youtube.", StringComparison.OrdinalIgnoreCase)
            || host.Contains("facebook.", StringComparison.OrdinalIgnoreCase)
            || host.Contains("google.", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".google", StringComparison.OrdinalIgnoreCase)
            || host.Contains("chatgpt.", StringComparison.OrdinalIgnoreCase)
            || host.Contains("zhihu.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(companyName)
            && LooksUnrelatedDirectoryHit(uri, companyName))
        {
            return false;
        }

        return true;
    }

    public static bool IsPlausiblePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        var trimmed = phone.Trim();
        if (YearLikePhoneRegex.IsMatch(trimmed))
        {
            return false;
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length is < 8 or > 15)
        {
            return false;
        }

        if (digits.StartsWith("20", StringComparison.Ordinal) && digits.Length <= 10)
        {
            return false;
        }

        if (digits.StartsWith("07", StringComparison.Ordinal) && digits.Length == 10)
        {
            return false;
        }

        return true;
    }

    public static bool IsPlausibleEmail(string? email, string? companyName, string? website)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return false;
        }

        var domain = email.Split('@').LastOrDefault()?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        if (BlockedHosts.Contains(domain)
            || domain.Contains("wikipedia.", StringComparison.OrdinalIgnoreCase)
            || domain.Contains("gmail.com", StringComparison.OrdinalIgnoreCase)
            || domain.Contains("yahoo.", StringComparison.OrdinalIgnoreCase)
            || domain.Contains("hotmail.", StringComparison.OrdinalIgnoreCase)
            || domain.Contains("outlook.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(website)
            && Uri.TryCreate(website, UriKind.Absolute, out var siteUri))
        {
            var siteHost = siteUri.Host.TrimStart("www.".ToCharArray()).ToLowerInvariant();
            if (domain.Contains(siteHost, StringComparison.OrdinalIgnoreCase)
                || siteHost.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return true;
    }

    public static bool HitMatchesBusiness(string? companyName, string? title, string? snippet, string? url)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return false;
        }

        var tokens = Tokenize(companyName);
        if (tokens.Count == 0)
        {
            return false;
        }

        var haystack = $"{title} {snippet} {url}".ToLowerInvariant();
        var matched = 0;
        foreach (var token in tokens)
        {
            if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                matched++;
            }
        }

        var required = tokens.Count <= 2 ? 1 : Math.Min(2, tokens.Count);
        return matched >= required;
    }

    public static bool LooksLikeOfficialBusinessSite(
        string? companyName,
        string? title,
        string? snippet,
        string? url)
    {
        if (!IsPlausibleWebsite(url, companyName))
        {
            return false;
        }

        if (!HitMatchesBusiness(companyName, title, snippet, url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && !Uri.TryCreate("https://" + url!.Trim(), UriKind.Absolute, out uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant().TrimStart("www.".ToCharArray());
        var path = uri.AbsolutePath ?? string.Empty;
        if (path.Contains("/wiki/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/threads/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/watch", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/baby-names/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/answers/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/question/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/chrome", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (host.Contains("google.", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".google", StringComparison.OrdinalIgnoreCase)
            || host.Contains("facebook.", StringComparison.OrdinalIgnoreCase)
            || host.Contains("youtube.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tokens = Tokenize(companyName ?? string.Empty);
        foreach (var token in tokens)
        {
            if (host.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var homepageLike = path is "/" or ""
                           || path.Equals("/en", StringComparison.OrdinalIgnoreCase)
                           || path.Equals("/ar", StringComparison.OrdinalIgnoreCase)
                           || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)
                           || path.Equals("/home", StringComparison.OrdinalIgnoreCase)
                           || path.Equals("/contact", StringComparison.OrdinalIgnoreCase)
                           || path.Equals("/contact-us", StringComparison.OrdinalIgnoreCase)
                           || path.Equals("/about", StringComparison.OrdinalIgnoreCase)
                           || path.Equals("/about-us", StringComparison.OrdinalIgnoreCase);
        if (homepageLike)
        {
            return true;
        }

        return path.Length <= 24;
    }

    private static bool LooksUnrelatedDirectoryHit(Uri uri, string companyName)
    {
        var path = uri.AbsolutePath ?? string.Empty;
        if (path.Contains("/baby-names/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/lexicon/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/threads/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/watch", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokens = Tokenize(companyName);
        if (tokens.Count == 0)
        {
            return false;
        }

        var hostAndPath = $"{uri.Host}{path}".ToLowerInvariant();
        return tokens.Count(token => hostAndPath.Contains(token, StringComparison.OrdinalIgnoreCase)) == 0
               && path.Length > 40;
    }

    private static List<string> Tokenize(string companyName)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "school", "schools", "academy", "college", "high", "primary", "secondary",
            "international", "the", "and", "for", "of", "al", "el", "gym", "fitness",
            "hotel", "clinic", "center", "centre", "company", "group", "ltd", "llc",
            "inc", "co", "مدرسة", "مدارس", "أكاديمية", "كلية"
        };
        var result = new List<string>();
        foreach (var part in companyName.Split([' ', '-', '_', '/', '\\', ',', '.'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = part.Trim().ToLowerInvariant();
            if (token.Length < 3 || stop.Contains(token))
            {
                continue;
            }

            result.Add(token);
            if (result.Count >= 6)
            {
                break;
            }
        }

        return result;
    }
}
