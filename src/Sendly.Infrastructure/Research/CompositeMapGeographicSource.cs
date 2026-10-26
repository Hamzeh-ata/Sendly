using Sendly.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Sendly.Infrastructure.Research;

public class CompositeMapGeographicSource : IMapGeographicSource
{
    private readonly OverpassOpenStreetMapSource _overpass;
    private readonly NominatimMapGeographicSource _nominatim;
    private readonly GooglePlacesMapGeographicSource _googlePlaces;
    private readonly ILogger<CompositeMapGeographicSource> _logger;

    public CompositeMapGeographicSource(
        OverpassOpenStreetMapSource overpass,
        NominatimMapGeographicSource nominatim,
        GooglePlacesMapGeographicSource googlePlaces,
        ILogger<CompositeMapGeographicSource> logger)
    {
        _overpass = overpass;
        _nominatim = nominatim;
        _googlePlaces = googlePlaces;
        _logger = logger;
    }

    public string Name => "OpenStreetMapComposite";

    public bool IsEnabled
        => _overpass.IsEnabled || _nominatim.IsEnabled || _googlePlaces.IsEnabled;

    public async Task<MapSourceQueryResult> QueryAsync(
        MapSourceQueryRequest request,
        CancellationToken cancellationToken)
    {
        var osmResult = await QueryOsmAsync(request, cancellationToken);
        var googleResult = await QueryGoogleSafeAsync(request, cancellationToken);
        return MergeResults(osmResult, googleResult);
    }

    private async Task<MapSourceQueryResult?> QueryOsmAsync(
        MapSourceQueryRequest request,
        CancellationToken cancellationToken)
    {
        MapSourceQueryResult? overpassResult = null;
        if (_overpass.IsEnabled)
        {
            overpassResult = await _overpass.QueryAsync(request, cancellationToken);
            if (overpassResult.Succeeded && overpassResult.Places.Count > 0)
            {
                return overpassResult;
            }

            if (overpassResult.Succeeded)
            {
                _logger.LogInformation("Overpass returned zero places; trying Nominatim fallback.");
            }
            else
            {
                _logger.LogWarning(
                    "Overpass map query failed ({Summary}); falling back to Nominatim.",
                    overpassResult.ErrorSummary);
            }
        }

        if (_nominatim.IsEnabled)
        {
            var nominatimResult = await _nominatim.QueryAsync(request, cancellationToken);
            if (nominatimResult.Succeeded && nominatimResult.Places.Count > 0)
            {
                return nominatimResult;
            }

            if (overpassResult?.Succeeded == true)
            {
                return overpassResult;
            }

            if (nominatimResult.Succeeded)
            {
                return nominatimResult;
            }

            return MergeFailure(overpassResult, nominatimResult);
        }

        return overpassResult;
    }

    private async Task<MapSourceQueryResult?> QueryGoogleSafeAsync(
        MapSourceQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!_googlePlaces.IsEnabled)
        {
            return null;
        }

        try
        {
            var result = await _googlePlaces.QueryAsync(request, cancellationToken);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Google Places map query failed ({Summary}); continuing without it.",
                    result.ErrorSummary);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Places map query threw; continuing without it.");
            return new MapSourceQueryResult
            {
                Succeeded = false,
                ErrorSummary = "Google Places failed unexpectedly."
            };
        }
    }

    private static MapSourceQueryResult MergeResults(
        MapSourceQueryResult? osmResult,
        MapSourceQueryResult? googleResult)
    {
        var places = new List<MapPlaceRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AppendUnique(places, seen, osmResult);
        AppendUnique(places, seen, googleResult);

        if (places.Count > 0)
        {
            return new MapSourceQueryResult
            {
                Succeeded = true,
                Places = places,
                ShouldSubdivide = osmResult?.ShouldSubdivide == true
            };
        }

        if (osmResult?.Succeeded == true)
        {
            return osmResult;
        }

        if (googleResult?.Succeeded == true)
        {
            return googleResult;
        }

        if (osmResult is not null)
        {
            return osmResult;
        }

        if (googleResult is not null)
        {
            return googleResult;
        }

        return new MapSourceQueryResult
        {
            Succeeded = false,
            ShouldSubdivide = true,
            ErrorSummary = "No geographic map sources are available."
        };
    }

    private static void AppendUnique(
        List<MapPlaceRecord> places,
        HashSet<string> seen,
        MapSourceQueryResult? result)
    {
        if (result?.Succeeded != true || result.Places.Count == 0)
        {
            return;
        }

        foreach (var place in result.Places)
        {
            var key = BuildMergeKey(place);
            if (!seen.Add(key))
            {
                continue;
            }

            places.Add(place);
        }
    }

    private static string BuildMergeKey(MapPlaceRecord place)
    {
        if (!string.IsNullOrWhiteSpace(place.SourceObjectKey))
        {
            return $"{place.SourceName}:{place.SourceObjectKey}";
        }

        var name = place.Name?.Trim() ?? string.Empty;
        var lat = place.Latitude?.ToString("0.000") ?? string.Empty;
        var lng = place.Longitude?.ToString("0.000") ?? string.Empty;
        return $"{name}|{lat}|{lng}";
    }

    private static MapSourceQueryResult MergeFailure(
        MapSourceQueryResult? overpassResult,
        MapSourceQueryResult nominatimResult)
    {
        if (overpassResult is null)
        {
            return nominatimResult;
        }

        return new MapSourceQueryResult
        {
            Succeeded = false,
            IsRateLimited = overpassResult.IsRateLimited || nominatimResult.IsRateLimited,
            IsUnsupported = overpassResult.IsUnsupported && nominatimResult.IsUnsupported,
            RetryAfterSeconds = overpassResult.RetryAfterSeconds ?? nominatimResult.RetryAfterSeconds,
            ShouldSubdivide = overpassResult.ShouldSubdivide || nominatimResult.ShouldSubdivide,
            ErrorSummary = overpassResult.ErrorSummary ?? nominatimResult.ErrorSummary
        };
    }
}
