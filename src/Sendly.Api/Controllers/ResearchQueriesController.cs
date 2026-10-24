using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/research-queries")]
public class ResearchQueriesController : ControllerBase
{
    private readonly ResearchQueryService _queryService;

    public ResearchQueriesController(ResearchQueryService queryService)
    {
        _queryService = queryService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ResearchQuerySummaryDto>>> List(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _queryService.ListByProjectAsync(projectId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchQueryDetailDto>> GetById(
        Guid projectId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var query = await _queryService.GetAsync(id, cancellationToken);
        if (query is null || query.ProjectId != projectId)
        {
            return NotFound(new { message = "Research query was not found." });
        }

        return Ok(query);
    }

    [HttpPost]
    public async Task<ActionResult<ResearchQueryDetailDto>> Create(
        Guid projectId,
        [FromBody] UpsertResearchQueryDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = await _queryService.CreateAsync(projectId, request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { projectId, id = query.Id }, query);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ResearchQueryDetailDto>> Update(
        Guid projectId,
        Guid id,
        [FromBody] UpsertResearchQueryDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _queryService.GetAsync(id, cancellationToken);
            if (existing is null || existing.ProjectId != projectId)
            {
                return NotFound(new { message = "Research query was not found." });
            }

            return Ok(await _queryService.UpdateAsync(id, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<ResearchQueryDetailDto>> Duplicate(
        Guid projectId,
        Guid id,
        [FromBody] DuplicateResearchQueryDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _queryService.GetAsync(id, cancellationToken);
            if (existing is null || existing.ProjectId != projectId)
            {
                return NotFound(new { message = "Research query was not found." });
            }

            var copy = await _queryService.DuplicateAsync(id, request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { projectId, id = copy.Id }, copy);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(
        Guid projectId,
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _queryService.GetAsync(id, cancellationToken);
            if (existing is null || existing.ProjectId != projectId)
            {
                return NotFound(new { message = "Research query was not found." });
            }

            await _queryService.ArchiveAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid projectId,
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _queryService.GetAsync(id, cancellationToken);
            if (existing is null || existing.ProjectId != projectId)
            {
                return NotFound(new { message = "Research query was not found." });
            }

            await _queryService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
