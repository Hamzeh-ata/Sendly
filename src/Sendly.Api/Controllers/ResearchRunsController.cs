using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/research-runs")]
public class ResearchRunsController : ControllerBase
{
    private readonly ResearchRunService _researchRunService;

    public ResearchRunsController(ResearchRunService researchRunService)
    {
        _researchRunService = researchRunService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ResearchRunSummaryDto>>> List(
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? templateId,
        CancellationToken cancellationToken)
    {
        return Ok(await _researchRunService.ListAsync(projectId, templateId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchRunDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var run = await _researchRunService.GetAsync(id, cancellationToken);
        if (run is null)
        {
            return NotFound(new { message = "Research run was not found." });
        }

        return Ok(run);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _researchRunService.CancelAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _researchRunService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
