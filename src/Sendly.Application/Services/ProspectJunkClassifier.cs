using System.Text.RegularExpressions;

namespace Sendly.Application.Services;

public static class ProspectJunkClassifier
{
    private static readonly Regex AllCapsHeadingRegex = new(
        @"^[A-Z0-9 ,.'’\-:]{12,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] LegalHeadingHints =
    [
        "important information", "who we are", "personal data", "privacy policy",
        "terms of", "cookie", "legal notice", "disclaimer", "how we use",
        "the data we collect", "your rights", "gdpr"
    ];

    private static readonly string[] MarketplaceHints =
    [
        "make offer", "minimum price", "buy now", "add to cart", "for sale",
        "seller's minimum", "domain is for sale", "you will be redirected"
    ];

    private static readonly string[] NavigationHints =
    [
        "advanced search", "search for", "sign in", "log in", "my account",
        "use cases", "pricing", "rss", "subscribe", "newsletter"
    ];

    private static readonly HashSet<string> NonBusinessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "facebook", "instagram", "linkedin", "twitter", "youtube", "tiktok", "pinterest",
        "reddit", "wikipedia", "google", "bing", "yahoo", "duckduckgo", "accueil", "islam",
        "home page", "welcome"
    };

    private static readonly string[] ForumInstructionHints =
    [
        "show posts by this member", "posts by this member", "view user profile",
        "member profile", "forum posts by", "find all posts by"
    ];

    public static bool IsJunkOrganization(string? name, string? sourceUrl = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        if (CompanyIdentityResolver.IsGenericName(name))
        {
            return true;
        }

        var trimmed = name.Trim();
        if (NonBusinessNames.Contains(trimmed)
            || LooksLikeForumInstruction(trimmed)
            || LooksLikeForumUsername(trimmed, sourceUrl)
            || LooksLikeInternalIdentity(trimmed, sourceUrl)
            || LooksLikeSoftwareDownloadName(trimmed))
        {
            return true;
        }
        if (LooksLikeLegalHeading(trimmed)
            || LooksLikeMarketplaceCopy(trimmed)
            || LooksLikeNavigationLabel(trimmed)
            || LooksLikeSentence(trimmed)
            || AllCapsHeadingRegex.IsMatch(trimmed))
        {
            return true;
        }

        if (DomainHealthPolicy.IsParkingOrMarketplaceHost(sourceUrl)
            || DomainHealthPolicy.LooksLikeDomainSalePage(trimmed, description, sourceUrl))
        {
            return true;
        }

        return LooksLikeRegistryChrome(trimmed);
    }

    public static bool LooksLikeForumInstruction(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        foreach (var hint in ForumInstructionHints)
        {
            if (lower.Contains(hint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeForumUsername(string name, string? sourceUrl)
    {
        var url = (sourceUrl ?? string.Empty).ToLowerInvariant();
        var isForumContext = url.Contains("/forum", StringComparison.Ordinal)
                             || url.Contains("/member", StringComparison.Ordinal)
                             || url.Contains("/profile/", StringComparison.Ordinal)
                             || url.Contains("/user/", StringComparison.Ordinal)
                             || url.Contains("community.", StringComparison.Ordinal);
        if (!isForumContext)
        {
            return false;
        }

        var trimmed = name.Trim();
        return !trimmed.Contains(' ')
               && trimmed.Length >= 3;
    }

    public static bool LooksLikeInternalIdentity(string name, string? sourceUrl)
    {
        var url = (sourceUrl ?? string.Empty).ToLowerInvariant();
        if (url.Contains("sendly.local", StringComparison.Ordinal)
            || url.Contains("localhost", StringComparison.Ordinal)
            || url.Contains(".local/", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public static bool LooksLikeSoftwareDownloadName(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        return lower.EndsWith(" - download", StringComparison.Ordinal)
               || lower.EndsWith(" download", StringComparison.Ordinal)
               || lower.Contains("download app", StringComparison.Ordinal)
               || lower.EndsWith(" apk", StringComparison.Ordinal);
    }

    public static bool LooksLikeLegalHeading(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var hint in LegalHeadingHints)
        {
            if (lower.Contains(hint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeMarketplaceCopy(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var hint in MarketplaceHints)
        {
            if (lower.Contains(hint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeNavigationLabel(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        if (lower is "academia" or "government" or "purpose" or "advanced search"
            or "resources" or "partners" or "pricing")
        {
            return true;
        }

        foreach (var hint in NavigationHints)
        {
            if (lower.Equals(hint, StringComparison.Ordinal)
                || lower.StartsWith(hint + " ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeSentence(string name)
    {
        if (name.Length >= 80)
        {
            return true;
        }

        var words = name.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 10)
        {
            return true;
        }

        var lower = name.ToLowerInvariant();
        return lower.Contains(" must ", StringComparison.Ordinal)
               || lower.Contains(" your ", StringComparison.Ordinal)
               || lower.Contains(" we ", StringComparison.Ordinal)
               || lower.Contains(" you will ", StringComparison.Ordinal);
    }

    public static bool LooksLikeRegistryChrome(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("opencorporates", StringComparison.Ordinal)
               || lower.Contains("legal-entity", StringComparison.Ordinal)
               || lower.Contains("api calls", StringComparison.Ordinal)
               || lower.Contains("case studies", StringComparison.Ordinal)
               || lower.Contains("history and principles", StringComparison.Ordinal);
    }
}
