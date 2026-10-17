using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly OutreachProjectService _projectService;
    private readonly ResearchRunService _researchRunService;
    private readonly ProspectService _prospectService;

    public ProjectsController(
        OutreachProjectService projectService,
        ResearchRunService researchRunService,
        ProspectService prospectService)
    {
        _projectService = projectService;
        _researchRunService = researchRunService;
        _prospectService = prospectService;
    }

    [HttpGet]
    public async Task<ActionResult<List<OutreachProjectSummaryDto>>> List(
        [FromQuery] string? search,
        [FromQuery] Guid? templateId,
        CancellationToken cancellationToken)
    {
        return Ok(await _projectService.ListAsync(search, templateId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OutreachProjectDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var project = await _projectService.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound(new { message = "Project was not found." });
        }

        return Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<OutreachProjectDetailDto>> Create(
        [FromBody] UpsertOutreachProjectDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var project = await _projectService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = project.Id }, project);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OutreachProjectDetailDto>> Update(
        Guid id,
        [FromBody] UpsertOutreachProjectDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _projectService.UpdateAsync(id, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _projectService.DeactivateAsync(id, cancellationToken);
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
            await _projectService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/research")]
    public async Task<ActionResult<ResearchRunDetailDto>> StartResearch(
        Guid id,
        [FromBody] StartResearchRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await _researchRunService.StartAsync(id, request, cancellationToken);
            return Accepted(run);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/outreach/prepare")]
    public async Task<ActionResult<PrepareOutreachResponseDto>> PrepareOutreach(
        Guid id,
        [FromBody] PrepareOutreachRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _prospectService.PrepareOutreachAsync(id, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
