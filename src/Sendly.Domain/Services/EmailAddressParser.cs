using System.Text.RegularExpressions;

namespace Sendly.Domain.Services;

public static class EmailAddressParser
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static EmailParseResult Parse(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return new EmailParseResult([], []);
        }

        var tokens = SplitTokens(rawInput);
        var valid = new List<string>();
        var invalid = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in tokens)
        {
            ProcessToken(token, seen, valid, invalid);
        }

        return new EmailParseResult(valid, invalid);
    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return EmailRegex.IsMatch(email.Trim());
    }

    public static bool IsPlaceholderOrInternalEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return true;
        }

        var normalized = email.Trim().ToLowerInvariant();
        var parts = normalized.Split('@', 2);
        if (parts.Length != 2)
        {
            return true;
        }

        var local = parts[0];
        var domain = parts[1].TrimEnd('.');
        if (IsPlaceholderDomain(domain) || IsPlaceholderLocalPart(local))
        {
            return true;
        }

        return domain.Contains("placeholder", StringComparison.Ordinal)
               || domain.Contains("yourdomain", StringComparison.Ordinal)
               || domain.Contains("your-domain", StringComparison.Ordinal);
    }

    public static bool IsAcceptableBusinessEmail(string? email)
    {
        return !string.IsNullOrWhiteSpace(email)
               && IsValidEmail(email)
               && !IsPlaceholderOrInternalEmail(email);
    }

    private static readonly HashSet<string> FileExtensionTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "svg", "webp", "ico", "bmp",
        "css", "js", "json", "xml", "pdf", "zip", "mp4", "mp3"
    };

    private static bool IsPlaceholderDomain(string domain)
    {
        var lastDot = domain.LastIndexOf('.');
        if (lastDot >= 0 && FileExtensionTlds.Contains(domain[(lastDot + 1)..]))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(domain)
            || domain.Equals("localhost", StringComparison.Ordinal)
            || domain.EndsWith(".local", StringComparison.Ordinal)
            || domain.EndsWith(".localhost", StringComparison.Ordinal)
            || domain.EndsWith(".invalid", StringComparison.Ordinal)
            || domain.EndsWith(".test", StringComparison.Ordinal)
            || domain.Equals("example.com", StringComparison.Ordinal)
            || domain.Equals("example.org", StringComparison.Ordinal)
            || domain.Equals("example.net", StringComparison.Ordinal)
            || domain.Equals("domain.com", StringComparison.Ordinal)
            || domain.Equals("email.com", StringComparison.Ordinal)
            || domain.Equals("yourdomain.com", StringComparison.Ordinal)
            || domain.Equals("yourcompany.com", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool IsPlaceholderLocalPart(string local)
    {
        return local.Equals("example", StringComparison.Ordinal)
               || local.Equals("placeholder", StringComparison.Ordinal)
               || local.Equals("yourname", StringComparison.Ordinal)
               || local.Equals("youremail", StringComparison.Ordinal)
               || local.Equals("name", StringComparison.Ordinal)
                   && local.Length <= 4;
    }

    public static string Normalize(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static IEnumerable<string> SplitTokens(string rawInput)
    {
        var normalized = rawInput
            .Replace('\r', '\n')
            .Replace(',', '\n')
            .Replace(';', '\n');

        return normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void ProcessToken(
        string token,
        HashSet<string> seen,
        List<string> valid,
        List<string> invalid)
    {
        var email = token.Trim();
        if (email.Length == 0)
        {
            return;
        }

        if (!IsValidEmail(email))
        {
            if (!invalid.Contains(email, StringComparer.OrdinalIgnoreCase))
            {
                invalid.Add(email);
            }

            return;
        }

        if (seen.Add(Normalize(email)))
        {
            valid.Add(email);
        }
    }
}

public sealed class EmailParseResult
{
    public EmailParseResult(IReadOnlyList<string> validEmails, IReadOnlyList<string> invalidEmails)
    {
        ValidEmails = validEmails;
        InvalidEmails = invalidEmails;
    }

    public IReadOnlyList<string> ValidEmails { get; }

    public IReadOnlyList<string> InvalidEmails { get; }

    public int ValidCount => ValidEmails.Count;

    public int InvalidCount => InvalidEmails.Count;
}
