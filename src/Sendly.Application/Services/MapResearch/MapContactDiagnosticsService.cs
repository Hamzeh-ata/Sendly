using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services.MapResearch;

public class MapContactDiagnosticsService
{
    private readonly IMapGeographicSource _mapSource;
    private readonly ISendlyDbContext _db;

    public MapContactDiagnosticsService(IMapGeographicSource mapSource, ISendlyDbContext db)
    {
        _mapSource = mapSource;
        _db = db;
    }

    public async Task<MapContactDiagnosticsDto> DiagnoseAsync(
        Guid projectId,
        MapContactDiagnosticsRequestDto request,
        CancellationToken cancellationToken)
    {
        var report = new MapContactDiagnosticsDto
        {
            ProjectId = projectId,
            SourceName = _mapSource.Name,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        if (!_mapSource.IsEnabled)
        {
            report.Errors.Add("Map geographic source is disabled.");
            report.CompletedAtUtc = DateTimeOffset.UtcNow;
            return report;
        }

        var query = new MapSourceQueryRequest
        {
            South = request.South,
            West = request.West,
            North = request.North,
            East = request.East,
            SourceCategories = request.SourceCategories?.Where(x => !string.IsNullOrWhiteSpace(x)).Take(3).ToList()
                             ?? ["amenity=school"],
            NameKeywords = request.NameKeywords?.Where(x => !string.IsNullOrWhiteSpace(x)).Take(2).ToList() ?? [],
            KeywordFamilyKey = "diagnostics",
            CountryCode = request.CountryCode,
            Limit = Math.Clamp(request.Limit, 1, 25)
        };

        MapSourceQueryResult result;
        try
        {
            result = await _mapSource.QueryAsync(query, cancellationToken);
        }
        catch (Exception ex)
        {
            report.Errors.Add(ex.Message);
            report.CompletedAtUtc = DateTimeOffset.UtcNow;
            return report;
        }

        report.HttpSucceeded = result.Succeeded;
        report.SourceError = result.ErrorSummary;
        report.ElementsReceived = result.Places.Count;
        CountSourceTags(result.Places, report);
        CountMappedFields(result.Places, report);

        var persisted = 0;
        var samples = new List<MapContactDiagnosticSampleDto>();
        foreach (var place in result.Places.Take(10))
        {
            var sample = BuildSample(place);
            samples.Add(sample);
            if (sample.MappedPhone || sample.MappedEmail || sample.MappedWebsite)
            {
                persisted++;
            }
        }

        report.Samples = samples;
        report.FieldsMappedToInternalModel = CountMapped(result.Places);
        report.CompletedAtUtc = DateTimeOffset.UtcNow;

        var markers = await _db.Prospects.AsNoTracking()
            .Where(x => x.ProjectId == projectId
                        && x.Latitude != null
                        && x.Longitude != null
                        && x.Status != ProspectStatus.Rejected)
            .ToListAsync(cancellationToken);
        report.ProspectsWithPhone = markers.Count(x => !string.IsNullOrWhiteSpace(x.Phone));
        report.ProspectsWithEmail = markers.Count(x => !string.IsNullOrWhiteSpace(x.Email));
        report.ProspectsWithWebsite = markers.Count(x => !string.IsNullOrWhiteSpace(x.Website));
        report.ApiMarkersReturned = markers.Count(x =>
            !string.IsNullOrWhiteSpace(x.Phone)
            || !string.IsNullOrWhiteSpace(x.Email)
            || !string.IsNullOrWhiteSpace(x.Website));

        if (!result.Succeeded)
        {
            report.Errors.Add(result.ErrorSummary ?? "Source query failed.");
        }

        return report;
    }

    private static void CountSourceTags(IEnumerable<MapPlaceRecord> places, MapContactDiagnosticsDto report)
    {
        foreach (var place in places)
        {
            var tags = place.RawTags;
            if (tags.Count == 0 && !string.IsNullOrWhiteSpace(place.RawTagsJson))
            {
                try
                {
                    tags = JsonSerializer.Deserialize<Dictionary<string, string>>(place.RawTagsJson)
                           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                catch (JsonException)
                {
                    tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }

            Increment(report.ElementsWithTag, "phone", HasAny(tags, "phone", "contact:phone", "telephone", "mobile", "contact:mobile"));
            Increment(report.ElementsWithTag, "email", HasAny(tags, "email", "contact:email"));
            Increment(report.ElementsWithTag, "website", HasAny(tags, "website", "contact:website", "url"));
            Increment(report.ElementsWithTag, "fax", HasAny(tags, "fax", "contact:fax"));
            Increment(report.ElementsWithTag, "opening_hours", HasAny(tags, "opening_hours"));
            Increment(report.ElementsWithTag, "addr:street", HasAny(tags, "addr:street", "addr:full"));
        }
    }

    private static void CountMappedFields(IEnumerable<MapPlaceRecord> places, MapContactDiagnosticsDto report)
    {
        foreach (var place in places)
        {
            Increment(report.MappedFieldCounts, "phone", !string.IsNullOrWhiteSpace(place.Phone));
            Increment(report.MappedFieldCounts, "email", !string.IsNullOrWhiteSpace(place.Email));
            Increment(report.MappedFieldCounts, "website", !string.IsNullOrWhiteSpace(place.Website));
            Increment(report.MappedFieldCounts, "streetAddress", !string.IsNullOrWhiteSpace(place.StreetAddress));
            Increment(report.MappedFieldCounts, "openingHours", !string.IsNullOrWhiteSpace(place.OpeningHours));
            Increment(report.MappedFieldCounts, "rawTags", !string.IsNullOrWhiteSpace(place.RawTagsJson));
        }
    }

    private static int CountMapped(IEnumerable<MapPlaceRecord> places)
    {
        var count = 0;
        foreach (var place in places)
        {
            if (!string.IsNullOrWhiteSpace(place.Phone)
                || !string.IsNullOrWhiteSpace(place.Email)
                || !string.IsNullOrWhiteSpace(place.Website)
                || !string.IsNullOrWhiteSpace(place.StreetAddress))
            {
                count++;
            }
        }

        return count;
    }

    private static MapContactDiagnosticSampleDto BuildSample(MapPlaceRecord place)
    {
        var rawPhone = FirstTag(place, "phone", "contact:phone", "telephone", "mobile", "contact:mobile");
        var rawEmail = FirstTag(place, "email", "contact:email");
        var rawWebsite = FirstTag(place, "website", "contact:website", "url");
        return new MapContactDiagnosticSampleDto
        {
            SourceObjectKey = place.SourceObjectKey,
            Name = place.Name,
            RawPhoneTag = rawPhone,
            RawEmailTag = rawEmail,
            RawWebsiteTag = rawWebsite,
            MappedPhone = !string.IsNullOrWhiteSpace(place.Phone),
            MappedEmail = !string.IsNullOrWhiteSpace(place.Email),
            MappedWebsite = !string.IsNullOrWhiteSpace(place.Website),
            PersistedPhone = place.Phone,
            PersistedEmail = place.Email,
            PersistedWebsite = place.Website,
            DroppedFields = BuildDropped(rawPhone, place.Phone, rawEmail, place.Email, rawWebsite, place.Website)
        };
    }

    private static List<string> BuildDropped(
        string? rawPhone,
        string? mappedPhone,
        string? rawEmail,
        string? mappedEmail,
        string? rawWebsite,
        string? mappedWebsite)
    {
        var dropped = new List<string>();
        if (!string.IsNullOrWhiteSpace(rawPhone) && string.IsNullOrWhiteSpace(mappedPhone))
        {
            dropped.Add("phone");
        }

        if (!string.IsNullOrWhiteSpace(rawEmail) && string.IsNullOrWhiteSpace(mappedEmail))
        {
            dropped.Add("email");
        }

        if (!string.IsNullOrWhiteSpace(rawWebsite) && string.IsNullOrWhiteSpace(mappedWebsite))
        {
            dropped.Add("website");
        }

        return dropped;
    }

    private static string? FirstTag(MapPlaceRecord place, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (place.RawTags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return $"{key}={value}";
            }
        }

        return null;
    }

    private static bool HasAny(IReadOnlyDictionary<string, string> tags, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return false;
    }

    private static void Increment(Dictionary<string, int> counts, string key, bool condition)
    {
        if (!condition)
        {
            return;
        }

        counts.TryGetValue(key, out var current);
        counts[key] = current + 1;
    }
}
