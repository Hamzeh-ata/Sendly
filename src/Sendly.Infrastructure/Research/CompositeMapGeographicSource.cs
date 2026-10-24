using Sendly.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Sendly.Infrastructure.Research;

public class CompositeMapGeographicSource : IMapGeographicSource
{
    private readonly OverpassOpenStreetMapSource _overpass;
    private readonly NominatimMapGeographicSource _nominatim;
    private readonly ILogger<CompositeMapGeographicSource> _logger;

    public CompositeMapGeographicSource(
        OverpassOpenStreetMapSource overpass,
        NominatimMapGeographicSource nominatim,
        ILogger<CompositeMapGeographicSource> logger)
    {
        _overpass = overpass;
        _nominatim = nominatim;
        _logger = logger;
    }

    public string Name => "OpenStreetMapComposite";

    public bool IsEnabled => _overpass.IsEnabled || _nominatim.IsEnabled;

    public async Task<MapSourceQueryResult> QueryAsync(
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

            return overpassResult ?? nominatimResult;
        }

        return overpassResult ?? new MapSourceQueryResult
        {
            Succeeded = false,
            ErrorSummary = "No OpenStreetMap geographic sources are available."
        };
    }
}
