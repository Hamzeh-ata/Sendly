using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services.MapResearch;

public static class GeographicCoveragePlanner
{
    public static List<MapGeographicCellPlan> BuildInitialCells(
        ResearchSpecification spec,
        IReadOnlyList<MapKeywordFamily> families,
        IReadOnlyList<MapSourceCategoryMapping> mappings,
        int maxCells)
    {
        var countries = GeographicBoundsCatalog.ResolveMany(spec.TargetCountries);
        var cells = new List<MapGeographicCellPlan>();
        if (countries.Count == 0)
        {
            return cells;
        }

        var categories = CollectUniqueCategories(mappings);
        if (categories.Count == 0)
        {
            return cells;
        }

        var familyKey = ResolveCombinedFamilyKey(families);
        foreach (var country in countries)
        {
            var tiles = BuildPriorityTiles(country, spec, Math.Clamp(maxCells, 1, 12));
            foreach (var tile in tiles)
            {
                cells.Add(new MapGeographicCellPlan
                {
                    RegionLabel = tile.Label,
                    CountryCode = country.CountryCode,
                    South = tile.South,
                    West = tile.West,
                    North = tile.North,
                    East = tile.East,
                    KeywordFamilyKey = familyKey,
                    SourceName = "OpenStreetMapOverpass",
                    SourceCategories = categories
                });

                if (cells.Count >= maxCells)
                {
                    return cells;
                }
            }
        }

        return cells;
    }

    public static List<MapCoverageRegion> ToEntities(
        Guid projectId,
        Guid researchRunId,
        IReadOnlyList<MapGeographicCellPlan> cells)
    {
        var entities = new List<MapCoverageRegion>();
        foreach (var cell in cells)
        {
            entities.Add(new MapCoverageRegion
            {
                ProjectId = projectId,
                ResearchRunId = researchRunId,
                CountryCode = cell.CountryCode,
                RegionLabel = cell.RegionLabel,
                South = cell.South,
                West = cell.West,
                North = cell.North,
                East = cell.East,
                SourceName = cell.SourceName,
                KeywordFamilyKey = cell.KeywordFamilyKey,
                SourceCategoriesJson = System.Text.Json.JsonSerializer.Serialize(cell.SourceCategories),
                Status = MapCoverageRegionStatus.Pending
            });
        }

        return entities;
    }

    public static List<MapCoverageRegion> Subdivide(MapCoverageRegion region, int maxChildren = 4)
    {
        var midLat = (region.South + region.North) / 2d;
        var midLon = (region.West + region.East) / 2d;
        var children = new List<MapCoverageRegion>
        {
            Child(region, $"{region.RegionLabel}/SW", region.South, region.West, midLat, midLon),
            Child(region, $"{region.RegionLabel}/SE", region.South, midLon, midLat, region.East),
            Child(region, $"{region.RegionLabel}/NW", midLat, region.West, region.North, midLon),
            Child(region, $"{region.RegionLabel}/NE", midLat, midLon, region.North, region.East)
        };

        if (maxChildren < 4)
        {
            return children.Take(maxChildren).ToList();
        }

        return children;
    }

    public static bool IsOversized(MapCoverageRegion region, int recordCount, int softLimit = 80)
    {
        if (recordCount >= softLimit)
        {
            return true;
        }

        var latSpan = Math.Abs(region.North - region.South);
        var lonSpan = Math.Abs(region.East - region.West);
        return latSpan > 3.0 || lonSpan > 3.0;
    }

    public static bool CanSubdivide(MapCoverageRegion region, int currentRegionCount, int maxRegions)
    {
        if (currentRegionCount >= maxRegions)
        {
            return false;
        }

        if (CountSubdivisionDepth(region.RegionLabel) >= 1)
        {
            return false;
        }

        if (region.AttemptCount >= 2)
        {
            return false;
        }

        var latSpan = Math.Abs(region.North - region.South);
        var lonSpan = Math.Abs(region.East - region.West);
        return latSpan > 0.18 && lonSpan > 0.18;
    }

    public static int CountSubdivisionDepth(string regionLabel)
    {
        if (string.IsNullOrWhiteSpace(regionLabel))
        {
            return 0;
        }

        var depth = 0;
        foreach (var ch in regionLabel)
        {
            if (ch == '/')
            {
                depth++;
            }
        }

        return depth;
    }

    private static MapCoverageRegion Child(
        MapCoverageRegion parent,
        string label,
        double south,
        double west,
        double north,
        double east)
    {
        return new MapCoverageRegion
        {
            ProjectId = parent.ProjectId,
            ResearchRunId = parent.ResearchRunId,
            ParentRegionId = parent.Id,
            CountryCode = parent.CountryCode,
            RegionLabel = label,
            South = south,
            West = west,
            North = north,
            East = east,
            SourceName = parent.SourceName,
            KeywordFamilyKey = parent.KeywordFamilyKey,
            SourceCategoriesJson = parent.SourceCategoriesJson,
            Status = MapCoverageRegionStatus.Pending
        };
    }

    private static List<(string Label, double South, double West, double North, double East)> BuildPriorityTiles(
        GeographicBounds country,
        ResearchSpecification spec,
        int maxTiles)
    {
        var cityTiles = BuildCityTiles(country, spec.Cities, maxTiles);
        if (cityTiles.Count > 0)
        {
            return cityTiles;
        }

        return BuildCountryTiles(country, Math.Min(ResolveTileCount(country), maxTiles));
    }

    private static List<(string Label, double South, double West, double North, double East)> BuildCityTiles(
        GeographicBounds country,
        IReadOnlyList<string> cities,
        int maxTiles)
    {
        var tiles = new List<(string Label, double South, double West, double North, double East)>();
        foreach (var city in cities)
        {
            var bounds = CityBoundsCatalog.Resolve(country.CountryCode, city);
            if (bounds is null)
            {
                continue;
            }

            tiles.Add(($"{country.CountryCode}-{Sanitize(city)}", bounds.South, bounds.West, bounds.North, bounds.East));
            if (tiles.Count >= maxTiles)
            {
                return tiles;
            }
        }

        return tiles;
    }

    private static List<(string Label, double South, double West, double North, double East)> BuildCountryTiles(
        GeographicBounds country,
        int tileCount)
    {
        if (tileCount <= 1)
        {
            return
            [
                ($"{country.CountryCode}-1", country.South, country.West, country.North, country.East)
            ];
        }

        var side = (int)Math.Ceiling(Math.Sqrt(tileCount));
        var latStep = (country.North - country.South) / side;
        var lonStep = (country.East - country.West) / side;
        var tiles = new List<(string Label, double South, double West, double North, double East)>();
        var index = 1;
        for (var row = 0; row < side; row++)
        {
            for (var col = 0; col < side; col++)
            {
                var south = country.South + (row * latStep);
                var north = row == side - 1 ? country.North : south + latStep;
                var west = country.West + (col * lonStep);
                var east = col == side - 1 ? country.East : west + lonStep;
                tiles.Add(($"{country.CountryCode}-{index}", south, west, north, east));
                index++;
                if (tiles.Count >= tileCount)
                {
                    return tiles;
                }
            }
        }

        return tiles;
    }

    private static int ResolveTileCount(GeographicBounds country)
    {
        var latSpan = Math.Abs(country.North - country.South);
        var lonSpan = Math.Abs(country.East - country.West);
        var area = latSpan * lonSpan;
        if (area > 80)
        {
            return 9;
        }

        if (area > 20)
        {
            return 4;
        }

        return 1;
    }

    private static List<string> CollectUniqueCategories(IReadOnlyList<MapSourceCategoryMapping> mappings)
    {
        var categories = new List<string>();
        foreach (var mapping in mappings)
        {
            if (!mapping.IsSupported || string.IsNullOrWhiteSpace(mapping.SourceCategory))
            {
                continue;
            }

            if (!categories.Contains(mapping.SourceCategory, StringComparer.OrdinalIgnoreCase))
            {
                categories.Add(mapping.SourceCategory);
            }

            if (categories.Count >= 6)
            {
                break;
            }
        }

        return categories;
    }

    private static string ResolveCombinedFamilyKey(IReadOnlyList<MapKeywordFamily> families)
    {
        foreach (var family in families)
        {
            if (family.IsMandatory || family.IsPrimaryTarget)
            {
                return family.FamilyKey;
            }
        }

        return families.Count > 0 ? families[0].FamilyKey : "combined";
    }

    private static string Sanitize(string value)
        => new string(value.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
