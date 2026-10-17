using System.Text;
using System.Text.RegularExpressions;

namespace Sendly.Domain.Services;

public static class ProspectNormalizer
{
    private static readonly Regex NonAlphanumericRegex = new(
        @"[^\p{L}\p{Nd}\s]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MultiSpaceRegex = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] CompanyNoiseTokens =
    [
        "center", "centre", "club", "gym", "fitness", "llc", "ltd", "limited", "inc", "incorporated",
        "co", "company", "corp", "corporation", "group", "holding", "holdings", "salon", "studio",
        "the", "and"
    ];

    public static string NormalizeCompanyName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim().ToLowerInvariant();
        cleaned = NonAlphanumericRegex.Replace(cleaned, " ");
        cleaned = MultiSpaceRegex.Replace(cleaned, " ").Trim();
        return cleaned;
    }

    public static string NormalizeCompanyNameForMatch(string? value)
    {
        var normalized = NormalizeCompanyName(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = new List<string>();
        foreach (var part in parts)
        {
            if (CompanyNoiseTokens.Contains(part, StringComparer.Ordinal))
            {
                continue;
            }

            kept.Add(part);
        }

        return kept.Count == 0 ? normalized : string.Join(' ', kept);
    }

    public static bool AreCompanyNamesLikelySame(string? left, string? right)
    {
        var a = NormalizeCompanyName(left);
        var b = NormalizeCompanyName(right);
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        var matchA = NormalizeCompanyNameForMatch(left);
        var matchB = NormalizeCompanyNameForMatch(right);
        if (string.IsNullOrWhiteSpace(matchA) || string.IsNullOrWhiteSpace(matchB))
        {
            return false;
        }

        if (string.Equals(matchA, matchB, StringComparison.Ordinal))
        {
            return true;
        }

        return a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)
            || matchA.Contains(matchB, StringComparison.Ordinal) || matchB.Contains(matchA, StringComparison.Ordinal);
    }

    private static readonly HashSet<string> SocialOrDirectoryHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "instagram.com", "facebook.com", "fb.com", "linkedin.com", "twitter.com", "x.com",
        "tiktok.com", "youtube.com", "maps.google.com", "goo.gl", "bit.ly"
    };

    public static string? NormalizeWebsite(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var raw = value.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            raw = "https://" + raw;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        if (IsSocialOrDirectoryHost(host))
        {
            var path = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrWhiteSpace(path))
            {
                return host;
            }

            var firstSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstSegment)
                ? host
                : $"{host}/{firstSegment.ToLowerInvariant()}";
        }

        return host;
    }

    public static bool IsSocialOrDirectoryHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        foreach (var known in SocialOrDirectoryHosts)
        {
            if (string.Equals(host, known, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var raw = value.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            raw = "https://" + raw;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Scheme = Uri.UriSchemeHttps,
            Port = -1
        };

        var host = builder.Host.Trim().ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        builder.Host = host;
        if (builder.Path.EndsWith('/') && builder.Path.Length > 1)
        {
            builder.Path = builder.Path.TrimEnd('/');
        }

        return builder.Uri.GetLeftPart(UriPartial.Path).ToLowerInvariant();
    }

    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = ExtractDigits(value);
        if (digits.Length < 8)
        {
            return null;
        }

        return NormalizeSaudiOrInternationalPhone(digits);
    }

    public static string? NormalizeWhatsApp(string? value)
    {
        return NormalizePhone(value);
    }

    public static bool IsValidWhatsApp(string? value)
    {
        return NormalizeWhatsApp(value) is not null;
    }

    public static string? NormalizeCity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim().ToLowerInvariant();
        cleaned = NonAlphanumericRegex.Replace(cleaned, " ");
        cleaned = MultiSpaceRegex.Replace(cleaned, " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    public static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return EmailAddressParser.Normalize(value);
    }

    private static string NormalizeSaudiOrInternationalPhone(string digits)
    {
        if (digits.StartsWith("00", StringComparison.Ordinal) && digits.Length > 2)
        {
            digits = digits[2..];
        }

        if (digits.StartsWith("966", StringComparison.Ordinal) && digits.Length >= 12)
        {
            return digits;
        }

        if (digits.StartsWith('0') && digits.Length == 10)
        {
            return "966" + digits[1..];
        }

        if (digits.Length == 9 && (digits.StartsWith('5') || digits.StartsWith('1')))
        {
            return "966" + digits;
        }

        return digits;
    }

    private static string ExtractDigits(string value)
    {
        var buffer = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsDigit(ch))
            {
                buffer.Append(ch);
            }
        }

        return buffer.ToString();
    }
}
