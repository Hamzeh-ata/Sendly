using System.Text.RegularExpressions;

namespace Sendly.Domain.Services;

public static class TemplateMergeRenderer
{
    private static readonly Regex TagRegex = new(
        @"\{\{\s*([A-Za-z0-9_]+)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Render(string? template, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return TagRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return string.Empty;
        });
    }

    public static Dictionary<string, string> BuildValues(
        string? email,
        string? contactName,
        string? companyName,
        string? industry,
        string? city,
        string? country,
        string? contactRole,
        IReadOnlyDictionary<string, string>? extra)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add(values, "Email", email);
        Add(values, "ContactName", contactName);
        Add(values, "CompanyName", companyName);
        Add(values, "AgencyName", companyName);
        Add(values, "Industry", industry);
        Add(values, "City", city);
        Add(values, "Country", country);
        Add(values, "ContactRole", contactRole);

        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                Add(values, pair.Key, pair.Value);
            }
        }

        return values;
    }

    private static void Add(Dictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value.Trim();
        }
    }
}
