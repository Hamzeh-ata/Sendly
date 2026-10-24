using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Services;

namespace Sendly.Application.Services.MapResearch;

public static class MapPlaceDeduplicator
{
    public static bool IsSamePlace(MapPlaceRecord left, MapPlaceRecord right)
    {
        if (!string.IsNullOrWhiteSpace(left.SourceObjectKey)
            && string.Equals(left.SourceObjectKey, right.SourceObjectKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(left.OsmElementId)
            && string.Equals(left.OsmElementType, right.OsmElementType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.OsmElementId, right.OsmElementId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftDomain = UrlCanonicalizer.ExtractDomain(left.Website);
        var rightDomain = UrlCanonicalizer.ExtractDomain(right.Website);
        if (!string.IsNullOrWhiteSpace(leftDomain)
            && string.Equals(leftDomain, rightDomain, StringComparison.OrdinalIgnoreCase)
            && NamesSimilar(left.Name, right.Name))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(left.Phone)
            && NormalizePhone(left.Phone) == NormalizePhone(right.Phone)
            && NamesSimilar(left.Name, right.Name))
        {
            return true;
        }

        if (left.Latitude is not null
            && right.Latitude is not null
            && left.Longitude is not null
            && right.Longitude is not null
            && NamesSimilar(left.Name, right.Name)
            && DistanceMeters(left.Latitude.Value, left.Longitude.Value, right.Latitude.Value, right.Longitude.Value) <= 35)
        {
            return true;
        }

        return false;
    }

    public static Prospect? FindExistingProspect(IEnumerable<Prospect> existing, MapPlaceRecord place)
    {
        foreach (var prospect in existing)
        {
            if (!string.IsNullOrWhiteSpace(place.SourceObjectKey)
                && string.Equals(prospect.SourceObjectKey, place.SourceObjectKey, StringComparison.OrdinalIgnoreCase))
            {
                return prospect;
            }

            if (!string.IsNullOrWhiteSpace(place.OsmElementId)
                && string.Equals(prospect.OsmElementType, place.OsmElementType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(prospect.OsmElementId, place.OsmElementId, StringComparison.OrdinalIgnoreCase))
            {
                return prospect;
            }

            var placeDomain = UrlCanonicalizer.ExtractDomain(place.Website);
            if (!string.IsNullOrWhiteSpace(placeDomain)
                && string.Equals(prospect.NormalizedWebsite, placeDomain, StringComparison.OrdinalIgnoreCase)
                && NamesSimilar(prospect.CompanyName, place.Name))
            {
                return prospect;
            }

            if (prospect.Latitude is not null
                && prospect.Longitude is not null
                && place.Latitude is not null
                && place.Longitude is not null
                && NamesSimilar(prospect.CompanyName, place.Name)
                && DistanceMeters(
                    prospect.Latitude.Value,
                    prospect.Longitude.Value,
                    place.Latitude.Value,
                    place.Longitude.Value) <= 35)
            {
                return prospect;
            }
        }

        return null;
    }

    public static List<MapPlaceRecord> Deduplicate(IEnumerable<MapPlaceRecord> places)
    {
        var result = new List<MapPlaceRecord>();
        foreach (var place in places)
        {
            var duplicate = false;
            foreach (var existing in result)
            {
                if (!IsSamePlace(existing, place))
                {
                    continue;
                }

                MergeInto(existing, place);
                duplicate = true;
                break;
            }

            if (!duplicate)
            {
                result.Add(place);
            }
        }

        return result;
    }

    private static void MergeInto(MapPlaceRecord target, MapPlaceRecord incoming)
    {
        target.Website ??= incoming.Website;
        target.Phone ??= incoming.Phone;
        target.Email ??= incoming.Email;
        target.Fax ??= incoming.Fax;
        target.StreetAddress ??= incoming.StreetAddress;
        target.City ??= incoming.City;
        target.Postcode ??= incoming.Postcode;
        target.OpeningHours ??= incoming.OpeningHours;
        target.AlternativeName ??= incoming.AlternativeName;
        target.Operator ??= incoming.Operator;
        target.Description ??= incoming.Description;
        target.CategoryTags ??= incoming.CategoryTags;
        target.RawTagsJson ??= incoming.RawTagsJson;
        MergeList(target.Phones, incoming.Phones);
        MergeList(target.Emails, incoming.Emails);
        MergeList(target.Websites, incoming.Websites);
        MergeList(target.Faxes, incoming.Faxes);
        foreach (var pair in incoming.FieldProvenance)
        {
            if (!target.FieldProvenance.ContainsKey(pair.Key))
            {
                target.FieldProvenance[pair.Key] = pair.Value;
            }
        }

        if (target.Latitude is null && incoming.Latitude is not null)
        {
            target.Latitude = incoming.Latitude;
            target.Longitude = incoming.Longitude;
        }
    }

    private static void MergeList(List<string> target, IEnumerable<string> incoming)
    {
        var seen = new HashSet<string>(target, StringComparer.OrdinalIgnoreCase);
        foreach (var value in incoming)
        {
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                target.Add(value);
            }
        }
    }

    private static bool NamesSimilar(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var a = NormalizeName(left);
        var b = NormalizeName(right);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return a.Contains(b, StringComparison.OrdinalIgnoreCase)
               || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value)
        => new string(value.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray())
            .Trim()
            .ToLowerInvariant();

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        return new string(phone.Where(char.IsDigit).ToArray());
    }

    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6371000d;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadius * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
