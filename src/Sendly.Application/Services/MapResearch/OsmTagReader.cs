using System.Text.Json;

namespace Sendly.Application.Services.MapResearch;

public static class OsmTagReader
{
    public static Dictionary<string, string> FromJsonObject(JsonElement tags)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tags.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in tags.EnumerateObject())
        {
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result[property.Name] = value.Trim();
        }

        return result;
    }

    public static string? First(IReadOnlyDictionary<string, string> tags, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    public static List<string> AllValues(IReadOnlyDictionary<string, string> tags, params string[] keys)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (!tags.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var part in SplitMultiValue(raw))
            {
                if (seen.Add(part))
                {
                    result.Add(part);
                }
            }
        }

        return result;
    }

    public static string? FindKey(IReadOnlyDictionary<string, string> tags, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return key;
            }
        }

        return null;
    }

    public static List<string> SplitMultiValue(string raw)
    {
        var parts = raw.Split([';', '|', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                result.Add(part.Trim());
            }
        }

        return result;
    }
}
