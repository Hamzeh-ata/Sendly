using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Application.Services.MapResearch;
using Sendly.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class NominatimMapGeographicSource : IMapGeographicSource
{
    private static readonly Regex WeakStreetNameRegex = new(
        @"^(?:شارع|طريق|street|st\.|rd\.|road|avenue|ave\.)(?:\s|$)|^\d+[a-z]?\s*(?:شارع|street|st\.|rd\.|road)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResearchOptions _options;
    private readonly IChallengedSourceRegistry _challenges;
    private readonly ILogger<NominatimMapGeographicSource> _logger;
    private DateTimeOffset? _nextAllowedAtUtc;

    public NominatimMapGeographicSource(
        IHttpClientFactory httpClientFactory,
        IOptions<ResearchOptions> options,
        IChallengedSourceRegistry challenges,
        ILogger<NominatimMapGeographicSource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _challenges = challenges;
        _logger = logger;
    }

    public string Name => "OpenStreetMapNominatim";

    public bool IsEnabled
        => _options.EnableOpenStreetMapDiscovery
           && !_challenges.IsTemporarilyDisabled(Name, DateTimeOffset.UtcNow);

    public async Task<MapSourceQueryResult> QueryAsync(
        MapSourceQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return new MapSourceQueryResult
            {
                Succeeded = false,
                ErrorSummary = "OpenStreetMap Nominatim source is disabled or temporarily unavailable."
            };
        }

        var queries = BuildSearchQueries(request);
        if (queries.Count == 0)
        {
            return new MapSourceQueryResult
            {
                Succeeded = false,
                IsUnsupported = true,
                ErrorSummary = "No OSM categories or name keywords were provided for Nominatim."
            };
        }

        var client = _httpClientFactory.CreateClient("OpenStreetMap");
        var places = new List<MapPlaceRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MapSourceQueryResult? lastFailure = null;

        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForThrottleAsync(cancellationToken);
            try
            {
                var url = BuildSearchUrl(query, request);
                using var response = await client.GetAsync(url, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _nextAllowedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(1100);

                if (response.StatusCode is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.GatewayTimeout)
                {
                    lastFailure = new MapSourceQueryResult
                    {
                        Succeeded = false,
                        IsRateLimited = true,
                        RetryAfterSeconds = 5,
                        ErrorSummary = $"Nominatim capacity/limit HTTP {(int)response.StatusCode}."
                    };
                    continue;
                }

                var classification = HttpAccessResponseClassifier.Classify(response.StatusCode, body);
                if (classification.ShouldDisableSource || classification.IsChallenge)
                {
                    _challenges.RecordChallenge(
                        Name,
                        classification.Kind,
                        classification.Summary,
                        DateTimeOffset.UtcNow,
                        (int)response.StatusCode);
                    return new MapSourceQueryResult
                    {
                        Succeeded = false,
                        ErrorSummary = classification.Summary
                    };
                }

                if (!response.IsSuccessStatusCode)
                {
                    lastFailure = new MapSourceQueryResult
                    {
                        Succeeded = false,
                        ErrorSummary = $"Nominatim HTTP {(int)response.StatusCode}"
                    };
                    continue;
                }

                AppendPlaces(body, request, places, seen);
                if (places.Count >= Math.Clamp(request.Limit, 1, 50))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Nominatim map query timed out");
                lastFailure = new MapSourceQueryResult
                {
                    Succeeded = false,
                    IsRateLimited = true,
                    RetryAfterSeconds = 5,
                    ErrorSummary = "Nominatim timeout."
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nominatim map query failed");
                lastFailure = new MapSourceQueryResult
                {
                    Succeeded = false,
                    ErrorSummary = ex.Message
                };
            }
        }

        if (places.Count > 0)
        {
            _challenges.RecordSuccess(Name);
            return new MapSourceQueryResult
            {
                Succeeded = true,
                Places = places.Take(Math.Clamp(request.Limit, 1, 50)).ToList(),
                ShouldSubdivide = false
            };
        }

        if (lastFailure is not null)
        {
            return lastFailure;
        }

        return new MapSourceQueryResult
        {
            Succeeded = true,
            Places = [],
            ShouldSubdivide = false
        };
    }

    private async Task WaitForThrottleAsync(CancellationToken cancellationToken)
    {
        if (_nextAllowedAtUtc is null)
        {
            return;
        }

        var delay = _nextAllowedAtUtc.Value - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(delay, cancellationToken);
    }

    private static List<NominatimSearchQuery> BuildSearchQueries(MapSourceQueryRequest request)
    {
        var queries = new List<NominatimSearchQuery>();
        foreach (var category in request.SourceCategories.Take(2))
        {
            var parts = category.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                continue;
            }

            var key = parts[0].Trim().ToLowerInvariant();
            var value = parts[1].Trim().ToLowerInvariant();
            queries.Add(new NominatimSearchQuery
            {
                CategoryKey = key,
                CategoryValue = value,
                FreeText = null
            });

            var freeText = HumanizeTagValue(value);
            if (!string.IsNullOrWhiteSpace(freeText))
            {
                queries.Add(new NominatimSearchQuery
                {
                    CategoryKey = null,
                    CategoryValue = null,
                    FreeText = freeText
                });
            }
        }

        if (queries.Count > 0)
        {
            return queries.Take(4).ToList();
        }

        foreach (var keyword in request.NameKeywords.Take(2))
        {
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 3)
            {
                continue;
            }

            if (IsExcludedKeyword(keyword, request.ExcludedKeywords))
            {
                continue;
            }

            queries.Add(new NominatimSearchQuery
            {
                CategoryKey = null,
                CategoryValue = null,
                FreeText = keyword.Trim()
            });
        }

        return queries;
    }

    private static string HumanizeTagValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace('_', ' ').Trim();
    }

    private static string BuildSearchUrl(NominatimSearchQuery query, MapSourceQueryRequest request)
    {
        var limit = Math.Clamp(request.Limit, 1, 40).ToString(CultureInfo.InvariantCulture);
        var viewbox = string.Join(',',
            F(request.West),
            F(request.North),
            F(request.East),
            F(request.South));
        var baseUrl =
            "https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&extratags=1&bounded=1"
            + $"&limit={limit}&viewbox={Uri.EscapeDataString(viewbox)}";

        if (!string.IsNullOrWhiteSpace(query.CategoryKey) && !string.IsNullOrWhiteSpace(query.CategoryValue))
        {
            return $"{baseUrl}&{Uri.EscapeDataString(query.CategoryKey)}={Uri.EscapeDataString(query.CategoryValue)}";
        }

        var text = query.FreeText ?? "school";
        return $"{baseUrl}&q={Uri.EscapeDataString(text)}";
    }

    private static void AppendPlaces(
        string body,
        MapSourceQueryRequest request,
        List<MapPlaceRecord> places,
        HashSet<string> seen)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var place = ParsePlace(item, request);
            if (place is null)
            {
                continue;
            }

            if (!seen.Add(place.SourceObjectKey))
            {
                continue;
            }

            if (IsExcludedKeyword(place.Name, request.ExcludedKeywords)
                || IsExcludedKeyword(place.CategoryTags, request.ExcludedKeywords))
            {
                continue;
            }

            places.Add(place);
            if (places.Count >= Math.Clamp(request.Limit, 1, 50))
            {
                return;
            }
        }
    }

    private static MapPlaceRecord? ParsePlace(JsonElement item, MapSourceQueryRequest request)
    {
        var osmType = item.TryGetProperty("osm_type", out var typeEl) ? typeEl.GetString() : null;
        var osmId = item.TryGetProperty("osm_id", out var idEl) ? idEl.ToString() : null;
        if (string.IsNullOrWhiteSpace(osmType) || string.IsNullOrWhiteSpace(osmId))
        {
            return null;
        }

        if (!TryReadCoordinate(item, "lat", out var lat)
            || !TryReadCoordinate(item, "lon", out var lon))
        {
            return null;
        }

        var displayName = ReadDisplayName(item);
        if (string.IsNullOrWhiteSpace(displayName) || IsWeakPlaceName(displayName))
        {
            return null;
        }

        var tags = BuildTagDictionary(item);
        var categoryTags = BuildCategoryTags(item);
        if (!MatchesRequestedCategory(categoryTags, request.SourceCategories))
        {
            return null;
        }

        var elementType = NormalizeOsmType(osmType);
        var sourceUrl = $"https://www.openstreetmap.org/{elementType}/{osmId}";
        var sourceObjectKey = $"osm:{elementType}/{osmId}";
        var retrievedAt = DateTimeOffset.UtcNow;
        var place = new MapPlaceRecord
        {
            SourceName = "OpenStreetMapNominatim",
            SourceObjectKey = sourceObjectKey,
            OsmElementType = elementType,
            OsmElementId = osmId,
            Name = displayName.Trim(),
            Latitude = lat,
            Longitude = lon,
            SourceUrl = sourceUrl,
            KeywordFamilyKey = request.KeywordFamilyKey
        };

        OsmPlaceContactMapper.ApplyTags(place, tags, retrievedAt);
        if (string.IsNullOrWhiteSpace(place.Name))
        {
            place.Name = displayName.Trim();
            OsmPlaceContactMapper.AddProvenance(place, "name", place.Name, "name", retrievedAt);
        }

        if (IsWeakPlaceName(place.Name))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(place.CategoryTags))
        {
            place.CategoryTags = categoryTags;
        }

        place.MatchedSourceCategory = MatchCategory(place.CategoryTags, request.SourceCategories);
        OsmPlaceContactMapper.AddProvenance(
            place,
            "latitude",
            OsmPlaceContactMapper.FormatCoordinate(lat),
            null,
            retrievedAt);
        OsmPlaceContactMapper.AddProvenance(
            place,
            "longitude",
            OsmPlaceContactMapper.FormatCoordinate(lon),
            null,
            retrievedAt);
        return place;
    }

    private static Dictionary<string, string> BuildTagDictionary(JsonElement item)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (item.TryGetProperty("extratags", out var extra) && extra.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in OsmTagReader.FromJsonObject(extra))
            {
                tags[pair.Key] = pair.Value;
            }
        }

        if (item.TryGetProperty("name", out var nameEl))
        {
            var name = nameEl.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                tags["name"] = name.Trim();
            }
        }

        if (item.TryGetProperty("category", out var category)
            && item.TryGetProperty("type", out var type)
            && category.ValueKind == JsonValueKind.String
            && type.ValueKind == JsonValueKind.String)
        {
            var key = category.GetString();
            var value = type.GetString();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                tags[key] = value;
            }
        }

        if (item.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.Object)
        {
            AddAddressTag(tags, address, "road", "addr:street");
            AddAddressTag(tags, address, "house_number", "addr:housenumber");
            AddAddressTag(tags, address, "city", "addr:city");
            AddAddressTag(tags, address, "town", "addr:city");
            AddAddressTag(tags, address, "suburb", "addr:suburb");
            AddAddressTag(tags, address, "postcode", "addr:postcode");
            AddAddressTag(tags, address, "country", "addr:country");
        }

        return tags;
    }

    private static void AddAddressTag(
        IDictionary<string, string> tags,
        JsonElement address,
        string sourceKey,
        string targetKey)
    {
        if (!address.TryGetProperty(sourceKey, out var value))
        {
            return;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text) || tags.ContainsKey(targetKey))
        {
            return;
        }

        tags[targetKey] = text.Trim();
    }

    private static bool TryReadCoordinate(JsonElement item, string propertyName, out double value)
    {
        value = 0;
        if (!item.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String
            && double.TryParse(
                property.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        return false;
    }

    private static string? ReadDisplayName(JsonElement item)
    {
        if (item.TryGetProperty("name", out var nameEl))
        {
            var name = nameEl.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        if (!item.TryGetProperty("display_name", out var displayEl))
        {
            return null;
        }

        var display = displayEl.GetString();
        if (string.IsNullOrWhiteSpace(display))
        {
            return null;
        }

        var comma = display.IndexOf(',', StringComparison.Ordinal);
        return comma > 0 ? display[..comma].Trim() : display.Trim();
    }

    private static string BuildCategoryTags(JsonElement item)
    {
        var parts = new List<string>();
        if (item.TryGetProperty("category", out var category)
            && item.TryGetProperty("type", out var type)
            && category.ValueKind == JsonValueKind.String
            && type.ValueKind == JsonValueKind.String)
        {
            var key = category.GetString();
            var value = type.GetString();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{key}={value}");
            }
        }

        if (item.TryGetProperty("extratags", out var tags) && tags.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in tags.EnumerateObject())
            {
                if (property.Name is "amenity" or "shop" or "leisure" or "tourism" or "office" or "craft" or "sport" or "healthcare")
                {
                    parts.Add($"{property.Name}={property.Value.GetString()}");
                }
            }
        }

        return string.Join(';', parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool MatchesRequestedCategory(string? categoryTags, IReadOnlyList<string> requested)
    {
        if (requested.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(categoryTags))
        {
            return false;
        }

        foreach (var category in requested)
        {
            if (categoryTags.Contains(category, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? MatchCategory(string? categoryTags, IReadOnlyList<string> requested)
    {
        if (string.IsNullOrWhiteSpace(categoryTags))
        {
            return requested.FirstOrDefault();
        }

        foreach (var category in requested)
        {
            if (categoryTags.Contains(category, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return requested.FirstOrDefault();
    }

    private static bool IsWeakPlaceName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length < 3)
        {
            return true;
        }

        return WeakStreetNameRegex.IsMatch(trimmed);
    }

    private static string? ReadExtraTag(JsonElement item, string name)
    {
        if (!item.TryGetProperty("extratags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return tags.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private static string? ReadAddressField(JsonElement item, string name)
    {
        if (!item.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return address.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private static string? BuildStreet(JsonElement item)
    {
        var house = ReadAddressField(item, "house_number");
        var road = ReadAddressField(item, "road");
        if (string.IsNullOrWhiteSpace(house) && string.IsNullOrWhiteSpace(road))
        {
            return null;
        }

        return string.Join(' ', new[] { house, road }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string NormalizeOsmType(string osmType)
        => osmType.ToLowerInvariant() switch
        {
            "node" => "node",
            "way" => "way",
            "relation" => "relation",
            _ => osmType.ToLowerInvariant()
        };

    private static void AddProvenance(
        MapPlaceRecord place,
        string field,
        string? value,
        string sourceId,
        string sourceUrl,
        DateTimeOffset retrievedAt)
    {
        place.FieldProvenance[field] = new MapFieldProvenance
        {
            FieldName = field,
            Value = value,
            SourceIdentifier = sourceId,
            SourceUrl = sourceUrl,
            RetrievedAtUtc = retrievedAt,
            VerificationStatus = "source_reported"
        };
    }

    private static bool IsExcludedKeyword(string? haystack, IReadOnlyList<string> exclusions)
    {
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return false;
        }

        foreach (var exclusion in exclusions)
        {
            if (!string.IsNullOrWhiteSpace(exclusion)
                && haystack.Contains(exclusion, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string F(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    private sealed class NominatimSearchQuery
    {
        public string? CategoryKey { get; set; }

        public string? CategoryValue { get; set; }

        public string? FreeText { get; set; }
    }
}
