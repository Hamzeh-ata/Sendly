using System.Globalization;
using System.Net;
using System.Text;
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

public class OverpassOpenStreetMapSource : IMapGeographicSource
{
    private static readonly Regex RetryAfterRegex = new(@"(\d+)", RegexOptions.Compiled);
    private static readonly string[] Endpoints =
    [
        "https://overpass-api.de/api/interpreter",
        "https://lz4.overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter"
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResearchOptions _options;
    private readonly IChallengedSourceRegistry _challenges;
    private readonly ILogger<OverpassOpenStreetMapSource> _logger;
    private DateTimeOffset? _nextAllowedAtUtc;
    private int _endpointCursor;

    public OverpassOpenStreetMapSource(
        IHttpClientFactory httpClientFactory,
        IOptions<ResearchOptions> options,
        IChallengedSourceRegistry challenges,
        ILogger<OverpassOpenStreetMapSource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _challenges = challenges;
        _logger = logger;
    }

    public string Name => "OpenStreetMapOverpass";

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
                ErrorSummary = "OpenStreetMap Overpass source is disabled or temporarily unavailable."
            };
        }

        if (request.SourceCategories.Count == 0 && request.NameKeywords.Count == 0)
        {
            return new MapSourceQueryResult
            {
                Succeeded = false,
                IsUnsupported = true,
                ErrorSummary = "No OSM categories or name keywords were provided for this query."
            };
        }

        await WaitForThrottleAsync(cancellationToken);
        var query = BuildOverpassQuery(request);
        var client = _httpClientFactory.CreateClient("OpenStreetMapOverpass");
        MapSourceQueryResult? lastFailure = null;
        for (var attempt = 0; attempt < Endpoints.Length; attempt++)
        {
            var endpoint = Endpoints[(_endpointCursor + attempt) % Endpoints.Length];
            try
            {
                using var content = new StringContent(
                    $"data={Uri.EscapeDataString(query)}",
                    Encoding.UTF8,
                    "application/x-www-form-urlencoded");
                using var response = await client.PostAsync(endpoint, content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _nextAllowedAtUtc = DateTimeOffset.UtcNow.AddSeconds(2);

                if (response.StatusCode is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.GatewayTimeout)
                {
                    var retryAfter = ParseRetryAfter(response) ?? 20;
                    lastFailure = new MapSourceQueryResult
                    {
                        Succeeded = false,
                        IsRateLimited = true,
                        RetryAfterSeconds = retryAfter,
                        ErrorSummary = $"Overpass capacity/limit HTTP {(int)response.StatusCode} at {endpoint}."
                    };
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var classification = HttpAccessResponseClassifier.Classify(response.StatusCode, body);
                    if (classification.ShouldDisableSource || classification.IsChallenge)
                    {
                        _challenges.RecordChallenge(
                            Name,
                            classification.Kind,
                            classification.Summary,
                            DateTimeOffset.UtcNow,
                            (int)response.StatusCode);
                    }

                    var shouldSubdivide = (int)response.StatusCode == 400
                                          || body.Contains("query timed out", StringComparison.OrdinalIgnoreCase)
                                          || body.Contains("too many", StringComparison.OrdinalIgnoreCase);
                    lastFailure = new MapSourceQueryResult
                    {
                        Succeeded = false,
                        ShouldSubdivide = shouldSubdivide,
                        ErrorSummary = $"Overpass HTTP {(int)response.StatusCode}: {Trim(body, 240)}"
                    };
                    continue;
                }

                var places = ParseElements(body, request);
                _endpointCursor = (_endpointCursor + attempt) % Endpoints.Length;
                _challenges.RecordSuccess(Name);
                return new MapSourceQueryResult
                {
                    Succeeded = true,
                    Places = places,
                    ShouldSubdivide = places.Count >= Math.Max(40, request.Limit)
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex, "Overpass query timed out for {Endpoint}", endpoint);
                    lastFailure = new MapSourceQueryResult
                    {
                        Succeeded = false,
                        IsRateLimited = true,
                        RetryAfterSeconds = 5,
                        ShouldSubdivide = false,
                        ErrorSummary = $"Overpass timeout at {endpoint}."
                    };
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Overpass query failed for {Endpoint}", endpoint);
                    lastFailure = new MapSourceQueryResult
                    {
                        Succeeded = false,
                        ShouldSubdivide = false,
                        ErrorSummary = ex.Message
                    };
                }
        }

        return lastFailure ?? new MapSourceQueryResult
        {
            Succeeded = false,
            ErrorSummary = "All Overpass endpoints failed."
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

    private static string BuildOverpassQuery(MapSourceQueryRequest request)
    {
        var south = F(request.South);
        var west = F(request.West);
        var north = F(request.North);
        var east = F(request.East);
        var bbox = $"{south},{west},{north},{east}";
        var sb = new StringBuilder();
        sb.Append("[out:json][timeout:25];(");

        var hasCategories = false;
        foreach (var category in request.SourceCategories.Take(3))
        {
            var parts = category.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                continue;
            }

            var key = EscapeOverpass(parts[0]);
            var value = EscapeOverpass(parts[1]);
            sb.Append($"node[\"{key}\"=\"{value}\"]({bbox});");
            sb.Append($"way[\"{key}\"=\"{value}\"]({bbox});");
            sb.Append($"relation[\"{key}\"=\"{value}\"]({bbox});");
            hasCategories = true;
        }

        if (!hasCategories)
        {
            foreach (var keyword in request.NameKeywords.Take(2))
            {
                if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 3 || keyword.Length > 24)
                {
                    continue;
                }

                if (IsExcludedKeyword(keyword, request.ExcludedKeywords))
                {
                    continue;
                }

                var escaped = EscapeOverpassRegex(keyword.Trim());
                sb.Append($"node[\"name\"~\"{escaped}\",i]({bbox});");
                sb.Append($"way[\"name\"~\"{escaped}\",i]({bbox});");
                sb.Append($"relation[\"name\"~\"{escaped}\",i]({bbox});");
            }
        }

        sb.Append(");out center tags;");
        return sb.ToString();
    }

    private static List<MapPlaceRecord> ParseElements(string body, MapSourceQueryRequest request)
    {
        var result = new List<MapPlaceRecord>();
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("elements", out var elements)
            || elements.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var element in elements.EnumerateArray())
        {
            var place = ParseElement(element, request);
            if (place is null)
            {
                continue;
            }

            if (IsExcludedKeyword(place.Name, request.ExcludedKeywords)
                || IsExcludedKeyword(place.CategoryTags, request.ExcludedKeywords))
            {
                continue;
            }

            result.Add(place);
            if (result.Count >= Math.Clamp(request.Limit, 1, 100))
            {
                break;
            }
        }

        return result;
    }

    private static MapPlaceRecord? ParseElement(JsonElement element, MapSourceQueryRequest request)
    {
        var type = element.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        var id = element.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (!TryReadCoordinates(element, out var lat, out var lon))
        {
            return null;
        }

        if (!element.TryGetProperty("tags", out var tagsElement) || tagsElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var tags = OsmTagReader.FromJsonObject(tagsElement);
        var sourceUrl = $"https://www.openstreetmap.org/{type}/{id}";
        var retrievedAt = DateTimeOffset.UtcNow;
        var place = new MapPlaceRecord
        {
            SourceName = "OpenStreetMapOverpass",
            SourceObjectKey = $"osm:{type}/{id}",
            OsmElementType = type,
            OsmElementId = id,
            Latitude = lat,
            Longitude = lon,
            SourceUrl = sourceUrl,
            KeywordFamilyKey = request.KeywordFamilyKey
        };

        OsmPlaceContactMapper.ApplyTags(place, tags, retrievedAt);
        if (string.IsNullOrWhiteSpace(place.Name))
        {
            return null;
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

    private static bool TryReadCoordinates(JsonElement element, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        if (element.TryGetProperty("lat", out var latEl) && element.TryGetProperty("lon", out var lonEl))
        {
            return latEl.TryGetDouble(out lat) && lonEl.TryGetDouble(out lon);
        }

        if (element.TryGetProperty("center", out var center)
            && center.ValueKind == JsonValueKind.Object
            && center.TryGetProperty("lat", out var centerLat)
            && center.TryGetProperty("lon", out var centerLon))
        {
            return centerLat.TryGetDouble(out lat) && centerLon.TryGetDouble(out lon);
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

    private static int? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is not null)
        {
            return (int)Math.Clamp(response.Headers.RetryAfter.Delta.Value.TotalSeconds, 5, 300);
        }

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var match = RetryAfterRegex.Match(raw);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds))
                {
                    return Math.Clamp(seconds, 5, 300);
                }
            }
        }

        return null;
    }

    private static string EscapeOverpass(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeOverpassRegex(string value)
    {
        var escaped = Regex.Escape(value);
        return escaped.Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string F(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value[..max];
}
