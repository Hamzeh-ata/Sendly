using Sendly.Application.DTOs;
using Sendly.Application.Services.MapResearch;

namespace Sendly.Application.Services;

public class GeoCatalogService
{
    public List<GeoCountryDto> ListCountries()
    {
        var result = new List<GeoCountryDto>();
        foreach (var entry in GeographicBoundsCatalog.ListAll())
        {
            result.Add(new GeoCountryDto
            {
                Code = entry.CountryCode,
                Name = entry.CountryName,
                Cities = CityBoundsCatalog.ListCities(entry.CountryCode).ToList()
            });
        }

        return result;
    }
}
