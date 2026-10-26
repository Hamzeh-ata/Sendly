using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/geo")]
public class GeoController : ControllerBase
{
    private readonly GeoCatalogService _geoCatalog;

    public GeoController(GeoCatalogService geoCatalog)
    {
        _geoCatalog = geoCatalog;
    }

    [HttpGet("countries")]
    public ActionResult<List<GeoCountryDto>> ListCountries()
    {
        return Ok(_geoCatalog.ListCountries());
    }
}
