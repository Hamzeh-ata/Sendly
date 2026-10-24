namespace Sendly.Domain.Services;

public static class CountryScopeHelper
{
    public static string? InferCountryFromHost(string? host)
    {
        var tld = ExtractRegistrableTld(host);
        if (tld is null)
        {
            return null;
        }

        return tld switch
        {
            "ae" => "United Arab Emirates",
            "sa" => "Saudi Arabia",
            "eg" => "Egypt",
            "jo" => "Jordan",
            "uk" or "gb" => "United Kingdom",
            "us" => "United States",
            "ca" => "Canada",
            "au" => "Australia",
            "nz" => "New Zealand",
            "de" => "Germany",
            "fr" => "France",
            "in" => "India",
            "pk" => "Pakistan",
            "qa" => "Qatar",
            "kw" => "Kuwait",
            "bh" => "Bahrain",
            "om" => "Oman",
            "vn" => "Vietnam",
            "sg" => "Singapore",
            "my" => "Malaysia",
            "ch" => "Switzerland",
            "id" => "Indonesia",
            "jp" => "Japan",
            "th" => "Thailand",
            "ph" => "Philippines",
            _ => null
        };
    }

    public static bool HostMatchesCountries(string? host, IReadOnlyList<string> countries)
    {
        if (countries.Count == 0)
        {
            return true;
        }

        var inferred = InferCountryFromHost(host);
        if (inferred is null)
        {
            return true;
        }

        return CountryMatches(inferred, countries);
    }

    public static bool CountryMatches(string? country, IReadOnlyList<string> targets)
    {
        if (targets.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(country))
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            if (AreSameCountry(country, target))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesAnyCountry(string? evidence, IReadOnlyList<string> targets)
    {
        if (targets.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(evidence))
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            if (evidence.Contains(target, StringComparison.OrdinalIgnoreCase)
                || AreSameCountry(evidence, target))
            {
                return true;
            }

            var key = NormalizeCountryKey(target);
            if (!string.IsNullOrWhiteSpace(key)
                && evidence.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool AreSameCountry(string left, string right)
    {
        var a = NormalizeCountryKey(left);
        var b = NormalizeCountryKey(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        return a == b
               || a.Contains(b, StringComparison.Ordinal)
               || b.Contains(a, StringComparison.Ordinal);
    }

    public static string NormalizeCountryKey(string value)
    {
        var key = value.Trim().ToLowerInvariant();
        return key switch
        {
            "uae" or "ae" or "dubai" or "abu dhabi" or "sharjah" => "united arab emirates",
            "sa" or "ksa" or "saudi" => "saudi arabia",
            "uk" or "gb" or "britain" or "england" or "scotland" or "wales" => "united kingdom",
            "usa" or "us" or "america" => "united states",
            "nz" or "aotearoa" => "new zealand",
            "eg" => "egypt",
            "jo" => "jordan",
            "vn" or "viet nam" => "vietnam",
            "sg" => "singapore",
            "my" => "malaysia",
            "ch" or "swiss" => "switzerland",
            "id" => "indonesia",
            _ => key
        };
    }

    public static string? ExtractRegistrableTld(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalized = host.Trim().ToLowerInvariant();
        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? null : parts[^1];
    }

    private static readonly Dictionary<string, string> IsoPathCountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sa"] = "saudi arabia",
        ["ae"] = "united arab emirates",
        ["eg"] = "egypt",
        ["jo"] = "jordan",
        ["kw"] = "kuwait",
        ["bh"] = "bahrain",
        ["om"] = "oman",
        ["qa"] = "qatar",
        ["lb"] = "lebanon",
        ["jp"] = "japan",
        ["de"] = "germany",
        ["fr"] = "france",
        ["uk"] = "united kingdom",
        ["gb"] = "united kingdom",
        ["us"] = "united states",
        ["usa"] = "united states",
        ["ca"] = "canada",
        ["au"] = "australia",
        ["nz"] = "new zealand",
        ["in"] = "india",
        ["pk"] = "pakistan",
        ["tr"] = "turkey",
        ["cn"] = "china",
        ["kr"] = "south korea",
        ["vn"] = "vietnam",
        ["sg"] = "singapore",
        ["my"] = "malaysia",
        ["ch"] = "switzerland",
        ["id"] = "indonesia",
        ["th"] = "thailand",
        ["ph"] = "philippines",
        ["hk"] = "hong kong",
        ["tw"] = "taiwan",
        ["ie"] = "ireland",
        ["nl"] = "netherlands",
        ["es"] = "spain",
        ["it"] = "italy",
        ["br"] = "brazil",
        ["mx"] = "mexico"
    };

    private static readonly HashSet<string> DirectoryCountryPathMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "school", "schools", "company", "companies", "listing", "listings",
        "profile", "profiles", "directory", "org", "organization", "organisation"
    };

    public static string? InferCountryFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i].ToLowerInvariant();
            if (!IsoPathCountryCodes.TryGetValue(segment, out var mapped))
            {
                continue;
            }

            if (i == 0)
            {
                return ToDisplayCountry(mapped);
            }

            var previous = segments[i - 1].ToLowerInvariant();
            if (previous is "cm" or "country" or "countries" or "region" or "intl" or "international"
                || DirectoryCountryPathMarkers.Contains(previous))
            {
                return ToDisplayCountry(mapped);
            }

            var next = i + 1 < segments.Length ? segments[i + 1].ToLowerInvariant() : string.Empty;
            if (DirectoryCountryPathMarkers.Contains(next))
            {
                return ToDisplayCountry(mapped);
            }
        }

        return null;
    }

    public static bool PathLooksOutOfCountryScope(string? path, IReadOnlyList<string> countries)
    {
        if (string.IsNullOrWhiteSpace(path) || countries.Count == 0)
        {
            return false;
        }

        var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var country in countries)
        {
            if (!string.IsNullOrWhiteSpace(country))
            {
                targetKeys.Add(NormalizeCountryKey(country));
            }
        }

        if (targetKeys.Count == 0)
        {
            return false;
        }

        var inferred = InferCountryFromPath(path);
        if (inferred is null)
        {
            return false;
        }

        return !targetKeys.Contains(NormalizeCountryKey(inferred));
    }

    private static string ToDisplayCountry(string normalizedKey)
    {
        return normalizedKey switch
        {
            "united states" => "United States",
            "united kingdom" => "United Kingdom",
            "saudi arabia" => "Saudi Arabia",
            "united arab emirates" => "United Arab Emirates",
            "new zealand" => "New Zealand",
            "south korea" => "South Korea",
            "hong kong" => "Hong Kong",
            _ => char.ToUpperInvariant(normalizedKey[0]) + normalizedKey[1..]
        };
    }

    public static bool TextMentionsCountry(string? text, IReadOnlyList<string> countries)
    {
        if (string.IsNullOrWhiteSpace(text) || countries.Count == 0)
        {
            return false;
        }

        foreach (var country in countries)
        {
            if (string.IsNullOrWhiteSpace(country))
            {
                continue;
            }

            if (text.Contains(country, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var key = NormalizeCountryKey(country);
            if (key.Length >= 3 && text.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var alias in CountryAliases(key))
            {
                if (text.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> CountryAliases(string normalizedKey)
    {
        return normalizedKey switch
        {
            "united arab emirates" => ["uae", "dubai", "abu dhabi", "sharjah", "ajman"],
            "saudi arabia" => ["ksa", "riyadh", "jeddah"],
            "united kingdom" => ["uk", "london", "england", "scotland"],
            "united states" => ["usa", "america"],
            "vietnam" => ["viet nam", "hanoi", "ho chi minh"],
            "singapore" => ["sg"],
            "malaysia" => ["kuala lumpur"],
            "switzerland" => ["swiss", "bern", "geneva", "zurich"],
            _ => []
        };
    }
}
