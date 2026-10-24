using System.Globalization;
using System.Text.Json;
using Sendly.Application.Abstractions;

namespace Sendly.Application.Services.MapResearch;

public static class OsmPlaceContactMapper
{
    private static readonly string[] NameKeys =
    [
        "name", "official_name", "short_name", "alt_name", "name:en", "name:en-GB", "name:ar"
    ];

    private static readonly string[] PhoneKeys =
    [
        "phone", "contact:phone", "telephone", "mobile", "contact:mobile", "phone:mobile"
    ];

    private static readonly string[] EmailKeys =
    [
        "email", "contact:email"
    ];

    private static readonly string[] WebsiteKeys =
    [
        "website", "contact:website", "url", "website:en", "contact:website:en",
        "brand:website", "operator:website"
    ];

    private static readonly string[] FaxKeys =
    [
        "fax", "contact:fax"
    ];

    private static readonly string[] CategoryKeys =
    [
        "amenity", "shop", "leisure", "tourism", "office", "craft", "sport", "healthcare", "building"
    ];

    public static void ApplyTags(
        MapPlaceRecord place,
        IReadOnlyDictionary<string, string> tags,
        DateTimeOffset retrievedAtUtc)
    {
        place.RawTags = tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        place.RawTagsJson = JsonSerializer.Serialize(place.RawTags);

        var name = OsmTagReader.First(tags, NameKeys);
        if (!string.IsNullOrWhiteSpace(name))
        {
            place.Name = name;
            AddProvenance(place, "name", name, OsmTagReader.FindKey(tags, NameKeys), retrievedAtUtc);
        }

        place.AlternativeName ??= OsmTagReader.First(tags, "alt_name", "short_name", "official_name");
        place.Operator = OsmTagReader.First(tags, "operator", "brand");
        place.Description = OsmTagReader.First(tags, "description");
        place.OpeningHours = Prefer(place.OpeningHours, OsmTagReader.First(tags, "opening_hours"));
        if (!string.IsNullOrWhiteSpace(place.OpeningHours))
        {
            AddProvenance(place, "openingHours", place.OpeningHours, "opening_hours", retrievedAtUtc);
        }

        ApplyAddress(place, tags, retrievedAtUtc);
        ApplyContacts(place, tags, retrievedAtUtc);
        place.CategoryTags = BuildCategoryTags(tags);
    }

    public static void AddProvenance(
        MapPlaceRecord place,
        string fieldName,
        string? value,
        string? sourceTagKey,
        DateTimeOffset retrievedAtUtc,
        string extractionMethod = "structured_tag",
        string verificationStatus = "source_reported")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        place.FieldProvenance[fieldName] = new MapFieldProvenance
        {
            FieldName = fieldName,
            Value = value,
            OriginalValue = value,
            SourceTagKey = sourceTagKey,
            SourceIdentifier = place.SourceObjectKey,
            SourceUrl = place.SourceUrl,
            RetrievedAtUtc = retrievedAtUtc,
            ExtractionMethod = extractionMethod,
            VerificationStatus = verificationStatus
        };
    }

    private static void ApplyAddress(
        MapPlaceRecord place,
        IReadOnlyDictionary<string, string> tags,
        DateTimeOffset retrievedAtUtc)
    {
        var full = OsmTagReader.First(tags, "addr:full");
        var house = OsmTagReader.First(tags, "addr:housenumber");
        var street = OsmTagReader.First(tags, "addr:street");
        var composed = string.Join(' ', new[] { house, street }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var streetAddress = Prefer(place.StreetAddress, string.IsNullOrWhiteSpace(composed) ? full : composed);
        place.StreetAddress = streetAddress;
        place.City = Prefer(place.City, OsmTagReader.First(tags, "addr:city", "addr:suburb", "addr:town"));
        place.Postcode = Prefer(place.Postcode, OsmTagReader.First(tags, "addr:postcode"));
        place.Country = Prefer(place.Country, OsmTagReader.First(tags, "addr:country"));
        if (!string.IsNullOrWhiteSpace(streetAddress))
        {
            AddProvenance(
                place,
                "streetAddress",
                streetAddress,
                string.IsNullOrWhiteSpace(composed) ? "addr:full" : "addr:street",
                retrievedAtUtc);
        }
    }

    private static void ApplyContacts(
        MapPlaceRecord place,
        IReadOnlyDictionary<string, string> tags,
        DateTimeOffset retrievedAtUtc)
    {
        var phones = OsmTagReader.AllValues(tags, PhoneKeys)
            .Where(MapContactWebsiteGuard.IsPlausiblePhone)
            .ToList();
        MergeValues(place.Phones, phones);
        place.Phone = Prefer(place.Phone, phones.FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(place.Phone))
        {
            AddProvenance(place, "phone", place.Phone, OsmTagReader.FindKey(tags, PhoneKeys), retrievedAtUtc);
        }

        var emails = OsmTagReader.AllValues(tags, EmailKeys)
            .Where(x => MapContactWebsiteGuard.IsPlausibleEmail(x, place.Name, place.Website))
            .ToList();
        MergeValues(place.Emails, emails);
        place.Email = Prefer(place.Email, emails.FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(place.Email))
        {
            AddProvenance(place, "email", place.Email, OsmTagReader.FindKey(tags, EmailKeys), retrievedAtUtc);
        }

        var websites = OsmTagReader.AllValues(tags, WebsiteKeys)
            .Where(x => MapContactWebsiteGuard.IsPlausibleWebsite(x, place.Name))
            .ToList();
        MergeValues(place.Websites, websites);
        place.Website = Prefer(place.Website, websites.FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(place.Website))
        {
            AddProvenance(place, "website", place.Website, OsmTagReader.FindKey(tags, WebsiteKeys), retrievedAtUtc);
        }

        var faxes = OsmTagReader.AllValues(tags, FaxKeys);
        MergeValues(place.Faxes, faxes);
        place.Fax = Prefer(place.Fax, faxes.FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(place.Fax))
        {
            AddProvenance(place, "fax", place.Fax, OsmTagReader.FindKey(tags, FaxKeys), retrievedAtUtc);
        }
    }

    private static string BuildCategoryTags(IReadOnlyDictionary<string, string> tags)
    {
        var parts = new List<string>();
        foreach (var key in CategoryKeys)
        {
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{key}={value}");
            }
        }

        return string.Join(';', parts);
    }

    private static void MergeValues(List<string> target, IEnumerable<string> incoming)
    {
        var seen = new HashSet<string>(target, StringComparer.OrdinalIgnoreCase);
        foreach (var value in incoming)
        {
            if (seen.Add(value))
            {
                target.Add(value);
            }
        }
    }

    private static string? Prefer(string? existing, string? incoming)
        => string.IsNullOrWhiteSpace(existing) ? incoming : existing;

    public static string FormatCoordinate(double value)
        => value.ToString("0.#######", CultureInfo.InvariantCulture);
}
