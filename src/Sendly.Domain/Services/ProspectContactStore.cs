using System.Text.Json;

namespace Sendly.Domain.Services;

public static class ProspectContactStore
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
            return values is null ? [] : DeduplicateValues(values, x => x);
        }
        catch (JsonException)
        {
            return DeduplicateValues([json], x => x);
        }
    }

    public static string? Serialize(IEnumerable<string?> values, Func<string, string?> normalize)
    {
        var unique = DeduplicateValues(values, normalize);
        if (unique.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(unique, JsonOptions);
    }

    public static string? MergeEmails(string? existingJson, string? primaryEmail, IEnumerable<string?> additional)
    {
        var values = Parse(existingJson);
        if (!string.IsNullOrWhiteSpace(primaryEmail))
        {
            values.Add(primaryEmail);
        }

        foreach (var value in additional)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return Serialize(values, ProspectNormalizer.NormalizeEmail);
    }

    public static string? MergePhones(string? existingJson, string? primaryPhone, IEnumerable<string?> additional)
    {
        var values = Parse(existingJson);
        if (!string.IsNullOrWhiteSpace(primaryPhone))
        {
            values.Add(primaryPhone);
        }

        foreach (var value in additional)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return Serialize(values, ProspectNormalizer.NormalizePhone);
    }

    private static List<string> DeduplicateValues(IEnumerable<string?> values, Func<string, string?> normalize)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            var key = normalize(trimmed) ?? trimmed.ToLowerInvariant();
            if (seen.Add(key))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
