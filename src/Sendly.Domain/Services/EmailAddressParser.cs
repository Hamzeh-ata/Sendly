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

        if (seen.Add(email))
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
