using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/prospects")]
public class ProspectsController : ControllerBase
{
    private readonly ProspectService _prospectService;

    public ProspectsController(ProspectService prospectService)
    {
        _prospectService = prospectService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ProspectSummaryDto>>> List(
        [FromQuery] Guid? projectId,
        [FromQuery] string? search,
        [FromQuery] string? country,
        [FromQuery] string? industry,
        [FromQuery] string[]? contactMethod,
        [FromQuery] string[]? status,
        [FromQuery] string[]? qualification,
        [FromQuery] string[]? duplicate,
        CancellationToken cancellationToken)
    {
        var filter = new ProspectFilterDto
        {
            ProjectId = projectId,
            Search = search,
            Country = country,
            Industry = industry,
            ContactMethods = contactMethod,
            Statuses = status,
            Qualifications = qualification,
            Duplicates = duplicate
        };

        return Ok(await _prospectService.ListAsync(filter, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProspectDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var prospect = await _prospectService.GetAsync(id, cancellationToken);
        if (prospect is null)
        {
            return NotFound(new { message = "Prospect was not found." });
        }

        return Ok(prospect);
    }

    [HttpPost]
    public async Task<ActionResult<ProspectDetailDto>> Create(
        [FromBody] CreateProspectDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var prospect = await _prospectService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = prospect.Id }, prospect);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _prospectService.ApproveAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _prospectService.RejectAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/do-not-contact")]
    public async Task<IActionResult> DoNotContact(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _prospectService.MarkDoNotContactAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/recommend-template")]
    public async Task<ActionResult<RecommendTemplateResponseDto>> RecommendTemplate(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _prospectService.RecommendTemplateAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
