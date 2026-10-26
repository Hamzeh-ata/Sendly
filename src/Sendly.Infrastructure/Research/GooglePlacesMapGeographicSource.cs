using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class GooglePlacesMapGeographicSource : IMapGeographicSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string FieldMask =
        "places.id,places.displayName,places.formattedAddress,places.location,places.types,"
        + "places.websiteUri,places.nationalPhoneNumber,places.internationalPhoneNumber,places.businessStatus";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GooglePlacesOptions _options;
    private readonly ILogger<GooglePlacesMapGeographicSource> _logger;

    public GooglePlacesMapGeographicSource(
        IHttpClientFactory httpClientFactory,
        IOptions<GooglePlacesOptions> options,
        ILogger<GooglePlacesMapGeographicSource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "GooglePlaces";

    public bool IsEnabled
        => _options.Enabled
           && !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<MapSourceQueryResult> QueryAsync(
        MapSourceQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return new MapSourceQueryResult
            {
                Succeeded = false,
                ErrorSummary = "Google Places is disabled or missing an API key."
            };
        }

        var queries = BuildTextQueries(request);
        if (queries.Count == 0)
        {
            return new MapSourceQueryResult
            {
                Succeeded = false,
                IsUnsupported = true,
                ErrorSummary = "No Google Places text queries could be built from categories or keywords."
            };
        }

        var client = _httpClientFactory.CreateClient("GooglePlaces");
        var places = new List<MapPlaceRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MapSourceQueryResult? lastFailure = null;
        var delayMs = Math.Clamp(_options.RequestDelayMilliseconds, 0, 5000);
        var maxResults = Math.Clamp(_options.MaxResultsPerQuery, 1, 20);

        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var page = await SearchTextAsync(client, query, request, maxResults, cancellationToken);
                if (!page.Succeeded)
                {
                    lastFailure = page;
                    if (page.IsRateLimited)
                    {
                        break;
                    }

                    continue;
                }

                AppendPlaces(page.Places, request, places, seen);
                if (places.Count >= Math.Clamp(request.Limit, 1, 100))
                {
                    break;
                }

                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Places query failed for '{Query}'", query);
                lastFailure = new MapSourceQueryResult
                {
                    Succeeded = false,
                    ErrorSummary = Trim(ex.Message, 300)
                };
            }
        }

        if (places.Count > 0)
        {
            return new MapSourceQueryResult
            {
                Succeeded = true,
                Places = places
            };
        }

        return lastFailure ?? new MapSourceQueryResult
        {
            Succeeded = true,
            Places = []
        };
    }

    private async Task<MapSourceQueryResult> SearchTextAsync(
        HttpClient client,
        string textQuery,
        MapSourceQueryRequest request,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["textQuery"] = textQuery,
            ["maxResultCount"] = maxResults,
            ["locationRestriction"] = new
            {
                rectangle = new
                {
                    low = new
                    {
                        latitude = request.South,
                        longitude = request.West
                    },
                    high = new
                    {
                        latitude = request.North,
                        longitude = request.East
                    }
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl);
        httpRequest.Headers.TryAddWithoutValidation("X-Goog-Api-Key", _options.ApiKey!.Trim());
        httpRequest.Headers.TryAddWithoutValidation("X-Goog-FieldMask", FieldMask);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new MapSourceQueryResult
            {
                Succeeded = false,
                IsRateLimited = true,
                RetryAfterSeconds = 15,
                ErrorSummary = "Google Places rate limited."
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Google Places HTTP {Status}: {Body}",
                (int)response.StatusCode,
                Trim(body, 400));
            return new MapSourceQueryResult
            {
                Succeeded = false,
                ErrorSummary = $"Google Places HTTP {(int)response.StatusCode}"
            };
        }

        return new MapSourceQueryResult
        {
            Succeeded = true,
            Places = ParsePlaces(body, request)
        };
    }

    private List<string> BuildTextQueries(MapSourceQueryRequest request)
    {
        var queries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxQueries = Math.Clamp(_options.MaxQueriesPerRequest, 1, 8);

        foreach (var category in request.SourceCategories)
        {
            var term = HumanizeCategory(category);
            if (string.IsNullOrWhiteSpace(term) || IsExcluded(term, request.ExcludedKeywords))
            {
                continue;
            }

            TryAddQuery(queries, seen, term, maxQueries);
            if (queries.Count >= maxQueries)
            {
                return queries;
            }
        }

        foreach (var keyword in request.NameKeywords)
        {
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Trim().Length < 3)
            {
                continue;
            }

            if (IsExcluded(keyword, request.ExcludedKeywords))
            {
                continue;
            }

            TryAddQuery(queries, seen, keyword.Trim(), maxQueries);
            if (queries.Count >= maxQueries)
            {
                return queries;
            }
        }

        return queries;
    }

    private static void TryAddQuery(
        List<string> queries,
        HashSet<string> seen,
        string term,
        int maxQueries)
    {
        if (queries.Count >= maxQueries)
        {
            return;
        }

        if (!seen.Add(term))
        {
            return;
        }

        queries.Add(term);
    }

    private static string HumanizeCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return string.Empty;
        }

        var parts = category.Split('=', 2, StringSplitOptions.TrimEntries);
        var value = parts.Length == 2 ? parts[1] : category;
        return value.Replace('_', ' ').Trim();
    }

    private static bool IsExcluded(string term, List<string> excluded)
    {
        foreach (var item in excluded)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            if (term.Contains(item, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<MapPlaceRecord> ParsePlaces(string body, MapSourceQueryRequest request)
    {
        var places = new List<MapPlaceRecord>();
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("places", out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return places;
        }

        foreach (var item in array.EnumerateArray())
        {
            var place = MapPlace(item, request);
            if (place is null)
            {
                continue;
            }

            places.Add(place);
        }

        return places;
    }

    private static void AppendPlaces(
        List<MapPlaceRecord> incoming,
        MapSourceQueryRequest request,
        List<MapPlaceRecord> places,
        HashSet<string> seen)
    {
        foreach (var place in incoming)
        {
            if (places.Count >= Math.Clamp(request.Limit, 1, 100))
            {
                return;
            }

            var key = BuildDedupeKey(place);
            if (!seen.Add(key))
            {
                continue;
            }

            places.Add(place);
        }
    }

    private static MapPlaceRecord? MapPlace(JsonElement item, MapSourceQueryRequest request)
    {
        var name = ReadDisplayName(item);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (string.Equals(ReadString(item, "businessStatus"), "CLOSED_PERMANENTLY", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var placeId = ReadString(item, "id") ?? string.Empty;
        var website = ReadString(item, "websiteUri");
        var phone = ReadString(item, "internationalPhoneNumber")
                    ?? ReadString(item, "nationalPhoneNumber");
        var types = ReadTypes(item);
        double? lat = null;
        double? lng = null;
        if (item.TryGetProperty("location", out var location))
        {
            lat = ReadDouble(location, "latitude");
            lng = ReadDouble(location, "longitude");
        }

        var place = new MapPlaceRecord
        {
            SourceName = "GooglePlaces",
            SourceObjectKey = string.IsNullOrWhiteSpace(placeId) ? $"google:{name}" : placeId,
            OsmElementType = "google_place",
            OsmElementId = placeId,
            Name = name.Trim(),
            Latitude = lat,
            Longitude = lng,
            StreetAddress = ReadString(item, "formattedAddress"),
            Website = website,
            Phone = phone,
            CategoryTags = string.Join(';', types),
            MatchedSourceCategory = MatchCategory(types, request.SourceCategories),
            KeywordFamilyKey = request.KeywordFamilyKey,
            SourceUrl = string.IsNullOrWhiteSpace(placeId)
                ? null
                : $"https://www.google.com/maps/place/?q=place_id:{ExtractPlaceId(placeId)}",
            RawTagsJson = item.GetRawText()
        };

        if (!string.IsNullOrWhiteSpace(website))
        {
            place.Websites.Add(website);
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            place.Phones.Add(phone);
        }

        foreach (var type in types)
        {
            place.RawTags[$"type:{type}"] = "yes";
        }

        return place;
    }

    private static string? MatchCategory(List<string> types, List<string> sourceCategories)
    {
        foreach (var category in sourceCategories)
        {
            var humanized = HumanizeCategory(category);
            foreach (var type in types)
            {
                if (type.Contains(humanized.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)
                    || humanized.Contains(type.Replace('_', ' '), StringComparison.OrdinalIgnoreCase))
                {
                    return category;
                }
            }
        }

        return sourceCategories.Count > 0 ? sourceCategories[0] : null;
    }

    private static string ReadDisplayName(JsonElement item)
    {
        if (!item.TryGetProperty("displayName", out var displayName))
        {
            return string.Empty;
        }

        return ReadString(displayName, "text") ?? string.Empty;
    }

    private static List<string> ReadTypes(JsonElement item)
    {
        var types = new List<string>();
        if (!item.TryGetProperty("types", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return types;
        }

        foreach (var value in array.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    types.Add(text);
                }
            }
        }

        return types;
    }

    private static string? ReadString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static double? ReadDouble(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string BuildDedupeKey(MapPlaceRecord place)
    {
        if (!string.IsNullOrWhiteSpace(place.OsmElementId))
        {
            return place.OsmElementId;
        }

        var lat = place.Latitude?.ToString("0.0000", CultureInfo.InvariantCulture) ?? string.Empty;
        var lng = place.Longitude?.ToString("0.0000", CultureInfo.InvariantCulture) ?? string.Empty;
        return $"{place.Name}|{lat}|{lng}";
    }

    private static string ExtractPlaceId(string raw)
    {
        const string prefix = "places/";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return raw[prefix.Length..];
        }

        return raw;
    }

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
