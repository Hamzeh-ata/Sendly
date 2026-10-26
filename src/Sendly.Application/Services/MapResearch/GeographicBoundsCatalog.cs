namespace Sendly.Application.Services.MapResearch;

public class GeographicBounds
{
    public string CountryCode { get; set; } = string.Empty;

    public string CountryName { get; set; } = string.Empty;

    public double South { get; set; }

    public double West { get; set; }

    public double North { get; set; }

    public double East { get; set; }

    public List<string> Aliases { get; set; } = [];
}

public static class GeographicBoundsCatalog
{
    private static readonly List<GeographicBounds> Entries =
    [
        Bound("JO", "Jordan", 29.18, 34.95, 33.37, 39.30, "Hashemite Kingdom of Jordan"),
        Bound("AE", "United Arab Emirates", 22.63, 51.58, 26.08, 56.40, "UAE", "Emirates"),
        Bound("SA", "Saudi Arabia", 16.35, 34.50, 32.15, 55.67, "KSA", "Kingdom of Saudi Arabia"),
        Bound("EG", "Egypt", 22.00, 24.70, 31.67, 36.90),
        Bound("KW", "Kuwait", 28.52, 46.55, 30.09, 48.45),
        Bound("QA", "Qatar", 24.47, 50.75, 26.18, 51.63),
        Bound("BH", "Bahrain", 25.79, 50.45, 26.33, 50.66),
        Bound("OM", "Oman", 16.65, 52.00, 26.39, 59.84),
        Bound("LB", "Lebanon", 33.05, 35.10, 34.69, 36.63),
        Bound("IQ", "Iraq", 29.06, 38.79, 37.38, 48.57),
        Bound("US", "United States", 24.40, -125.00, 49.40, -66.90, "USA", "United States of America"),
        Bound("CA", "Canada", 41.68, -141.00, 83.11, -52.62),
        Bound("GB", "United Kingdom", 49.90, -8.20, 60.85, 1.77, "UK", "Britain", "Great Britain"),
        Bound("DE", "Germany", 47.27, 5.87, 55.06, 15.04),
        Bound("FR", "France", 41.33, -5.14, 51.09, 9.56),
        Bound("IN", "India", 6.75, 68.11, 35.67, 97.40),
        Bound("AU", "Australia", -43.65, 112.90, -10.05, 153.64)
    ];

    public static IReadOnlyList<GeographicBounds> ListAll()
    {
        return Entries;
    }

    public static GeographicBounds? Resolve(string? countryOrAlias)
    {
        if (string.IsNullOrWhiteSpace(countryOrAlias))
        {
            return null;
        }

        var needle = countryOrAlias.Trim();
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.CountryCode, needle, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.CountryName, needle, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }

            foreach (var alias in entry.Aliases)
            {
                if (string.Equals(alias, needle, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    public static string NormalizeName(string? countryOrAlias)
    {
        var bounds = Resolve(countryOrAlias);
        if (bounds is null)
        {
            return string.IsNullOrWhiteSpace(countryOrAlias) ? string.Empty : countryOrAlias.Trim();
        }

        return bounds.CountryName;
    }

    public static IReadOnlyList<GeographicBounds> ResolveMany(IEnumerable<string> countries)
    {
        var result = new List<GeographicBounds>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var country in countries)
        {
            var bounds = Resolve(country);
            if (bounds is null || !seen.Add(bounds.CountryCode))
            {
                continue;
            }

            result.Add(bounds);
        }

        return result;
    }

    private static GeographicBounds Bound(
        string code,
        string name,
        double south,
        double west,
        double north,
        double east,
        params string[] aliases)
    {
        return new GeographicBounds
        {
            CountryCode = code,
            CountryName = name,
            South = south,
            West = west,
            North = north,
            East = east,
            Aliases = aliases.ToList()
        };
    }
}
