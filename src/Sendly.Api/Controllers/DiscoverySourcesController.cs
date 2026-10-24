using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/discovery-sources")]
public class DiscoverySourcesController : ControllerBase
{
    private readonly DiscoverySourceRegistryService _registry;

    public DiscoverySourcesController(DiscoverySourceRegistryService registry)
    {
        _registry = registry;
    }

    [HttpGet]
    public async Task<ActionResult<List<DiscoverySourceSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var sources = await _registry.ListAsync(cancellationToken);
        return Ok(sources.Select(ResearchDiagnosticsService.MapSource).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DiscoverySourceSummaryDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var sources = await _registry.ListAsync(cancellationToken);
        var source = sources.FirstOrDefault(x => x.Id == id);
        if (source is null)
        {
            return NotFound(new { message = "Discovery source was not found." });
        }

        return Ok(ResearchDiagnosticsService.MapSource(source));
    }
}
