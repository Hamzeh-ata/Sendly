using Sendly.Application.DTOs;
using Sendly.Application.Services.MapResearch;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/map-research")]
public class MapResearchController : ControllerBase
{
    private readonly MapResearchService _mapResearchService;

    public MapResearchController(MapResearchService mapResearchService)
    {
        _mapResearchService = mapResearchService;
    }

    [HttpGet]
    public async Task<ActionResult<MapResearchOverviewDto>> Overview(
        Guid projectId,
        [FromQuery] Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        return Ok(await _mapResearchService.GetOverviewAsync(projectId, researchRunId, cancellationToken));
    }

    [HttpGet("markers")]
    public async Task<ActionResult<List<MapMarkerDto>>> Markers(
        Guid projectId,
        [FromQuery] Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        return Ok(await _mapResearchService.ListMarkersAsync(projectId, researchRunId, cancellationToken));
    }

    [HttpGet("plan-preview")]
    public async Task<ActionResult<MapResearchPlanPreviewDto>> PlanPreview(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mapResearchService.PreviewPlanAsync(projectId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("start")]
    public async Task<ActionResult<ResearchRunDetailDto>> Start(
        Guid projectId,
        [FromBody] StartMapResearchRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await _mapResearchService.StartAsync(projectId, request, cancellationToken);
            return Ok(run);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("runs/{researchRunId:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid projectId,
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mapResearchService.CancelAsync(researchRunId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("runs/{researchRunId:guid}/resume")]
    public async Task<IActionResult> Resume(
        Guid projectId,
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mapResearchService.ResumeAsync(researchRunId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("contact-diagnostics")]
    public async Task<ActionResult<MapContactDiagnosticsDto>> ContactDiagnostics(
        Guid projectId,
        [FromBody] MapContactDiagnosticsRequestDto request,
        CancellationToken cancellationToken)
    {
        return Ok(await _mapResearchService.DiagnoseContactsAsync(projectId, request, cancellationToken));
    }

    [HttpPost("reenrich-contacts")]
    public async Task<ActionResult<MapContactReenrichResultDto>> ReenrichContacts(
        Guid projectId,
        [FromQuery] Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mapResearchService.ReenrichContactsAsync(projectId, researchRunId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("prospects/{prospectId:guid}/extract-contacts-openai")]
    public async Task<ActionResult<MapOpenAiContactExtractResultDto>> ExtractContactsWithOpenAi(
        Guid projectId,
        Guid prospectId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mapResearchService.ExtractContactsWithOpenAiAsync(
                projectId,
                prospectId,
                cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("extract-contacts-openai")]
    public async Task<ActionResult<MapOpenAiContactBatchResultDto>> ExtractMissingContactsWithOpenAi(
        Guid projectId,
        [FromQuery] Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mapResearchService.ExtractMissingContactsWithOpenAiAsync(
                projectId,
                researchRunId,
                cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
