namespace Sendly.Application.Services.MapResearch;

public class CityBounds
{
    public double South { get; set; }

    public double West { get; set; }

    public double North { get; set; }

    public double East { get; set; }
}

public static class CityBoundsCatalog
{
    private static readonly Dictionary<string, Dictionary<string, CityBounds>> ByCountry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AE"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Dubai"] = Box(24.85, 54.90, 25.40, 55.60),
                ["Abu Dhabi"] = Box(24.20, 54.20, 24.70, 54.80),
                ["Sharjah"] = Box(25.20, 55.30, 25.45, 55.60),
                ["Ajman"] = Box(25.35, 55.40, 25.50, 55.55),
                ["Ras Al Khaimah"] = Box(25.65, 55.80, 25.90, 56.10),
                ["Fujairah"] = Box(25.05, 56.20, 25.25, 56.40),
                ["Al Ain"] = Box(24.05, 55.60, 24.35, 55.90)
            },
            ["JO"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Amman"] = Box(31.80, 35.75, 32.10, 36.10),
                ["Irbid"] = Box(32.45, 35.75, 32.65, 36.00),
                ["Zarqa"] = Box(32.00, 36.00, 32.15, 36.20),
                ["Aqaba"] = Box(29.45, 34.95, 29.60, 35.10)
            },
            ["SA"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Riyadh"] = Box(24.40, 46.40, 25.00, 47.10),
                ["Jeddah"] = Box(21.30, 39.00, 21.80, 39.40),
                ["Dammam"] = Box(26.25, 49.90, 26.55, 50.25),
                ["Khobar"] = Box(26.15, 50.05, 26.35, 50.30),
                ["Mecca"] = Box(21.30, 39.70, 21.55, 40.00),
                ["Medina"] = Box(24.35, 39.45, 24.60, 39.75)
            },
            ["US"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["New York"] = Box(40.60, -74.10, 40.90, -73.70),
                ["Los Angeles"] = Box(33.90, -118.50, 34.20, -118.10),
                ["Chicago"] = Box(41.75, -87.85, 42.00, -87.55),
                ["Houston"] = Box(29.65, -95.55, 29.90, -95.20),
                ["Miami"] = Box(25.70, -80.35, 25.90, -80.10),
                ["Dallas"] = Box(32.70, -97.00, 33.00, -96.65),
                ["San Francisco"] = Box(37.70, -122.50, 37.85, -122.35),
                ["Atlanta"] = Box(33.65, -84.50, 33.90, -84.25),
                ["Boston"] = Box(42.30, -71.15, 42.42, -70.95),
                ["Seattle"] = Box(47.55, -122.40, 47.70, -122.25),
                ["Austin"] = Box(30.20, -97.85, 30.40, -97.65),
                ["Denver"] = Box(39.65, -105.05, 39.80, -104.85)
            },
            ["CA"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Toronto"] = Box(43.60, -79.50, 43.80, -79.25),
                ["Vancouver"] = Box(49.20, -123.20, 49.35, -123.00),
                ["Montreal"] = Box(45.45, -73.70, 45.60, -73.50),
                ["Calgary"] = Box(50.95, -114.15, 51.15, -113.95),
                ["Edmonton"] = Box(53.45, -113.60, 53.60, -113.40),
                ["Ottawa"] = Box(45.35, -75.80, 45.48, -75.60)
            }
        };

    public static CityBounds? Resolve(string? countryCode, string? city)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(city))
        {
            return null;
        }

        if (!ByCountry.TryGetValue(countryCode.Trim(), out var cities))
        {
            return null;
        }

        if (cities.TryGetValue(city.Trim(), out var bounds))
        {
            return bounds;
        }

        foreach (var pair in cities)
        {
            if (pair.Key.Contains(city, StringComparison.OrdinalIgnoreCase)
                || city.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> ListCities(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return [];
        }

        if (!ByCountry.TryGetValue(countryCode.Trim(), out var cities))
        {
            return [];
        }

        var names = new List<string>();
        foreach (var pair in cities)
        {
            names.Add(pair.Key);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static string? NormalizeCityName(string? countryCode, string? city)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return null;
        }

        var trimmed = city.Trim();
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return trimmed;
        }

        if (!ByCountry.TryGetValue(countryCode.Trim(), out var cities))
        {
            return trimmed;
        }

        foreach (var pair in cities)
        {
            if (string.Equals(pair.Key, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        foreach (var pair in cities)
        {
            if (pair.Key.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        return trimmed;
    }

    private static CityBounds Box(double south, double west, double north, double east)
        => new()
        {
            South = south,
            West = west,
            North = north,
            East = east
        };
}
