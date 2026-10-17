using System.Text.Json;

namespace Sendly.Domain.Services;

public static class ProspectSourceUrlStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static List<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            return values is null ? [] : DeduplicateUrls(values);
        }
        catch (JsonException)
        {
            return DeduplicateUrls([json]);
        }
    }

    public static string? Serialize(IEnumerable<string?> urls)
    {
        var unique = DeduplicateUrls(urls);
        if (unique.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(unique, JsonOptions);
    }

    public static string? Merge(string? existingJson, IEnumerable<string?> additionalUrls)
    {
        var merged = Parse(existingJson);
        foreach (var url in additionalUrls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            merged.Add(url);
        }

        return Serialize(merged);
    }

    public static List<string> DeduplicateUrls(IEnumerable<string?> urls)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var trimmed = url.Trim();
            var key = ProspectNormalizer.NormalizeUrl(trimmed) ?? trimmed.ToLowerInvariant();
            if (seen.Add(key))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
