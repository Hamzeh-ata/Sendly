using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/sequences")]
public class SequencesController : ControllerBase
{
    private readonly OutreachSequenceService _sequenceService;

    public SequencesController(OutreachSequenceService sequenceService)
    {
        _sequenceService = sequenceService;
    }

    [HttpGet]
    public async Task<ActionResult<List<OutreachSequenceSummaryDto>>> List(
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        return Ok(await _sequenceService.ListAsync(projectId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OutreachSequenceDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var sequence = await _sequenceService.GetAsync(id, cancellationToken);
        if (sequence is null)
        {
            return NotFound(new { message = "Sequence was not found." });
        }

        return Ok(sequence);
    }

    [HttpPost("projects/{projectId:guid}")]
    public async Task<ActionResult<OutreachSequenceDetailDto>> Create(
        Guid projectId,
        [FromBody] UpsertOutreachSequenceDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var sequence = await _sequenceService.CreateAsync(projectId, request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = sequence.Id }, sequence);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OutreachSequenceDetailDto>> Update(
        Guid id,
        [FromBody] UpsertOutreachSequenceDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _sequenceService.UpdateAsync(id, request, cancellationToken));
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
            await _sequenceService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/analytics")]
    public async Task<ActionResult<SequenceAnalyticsDto>> GetAnalytics(Guid id, CancellationToken cancellationToken)
    {
        var analytics = await _sequenceService.GetAnalyticsAsync(id, cancellationToken);
        if (analytics is null)
        {
            return NotFound(new { message = "Sequence was not found." });
        }

        return Ok(analytics);
    }

    [HttpPost("{id:guid}/enroll")]
    public async Task<ActionResult<List<SequenceEnrollmentDto>>> Enroll(
        Guid id,
        [FromBody] EnrollProspectsRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _sequenceService.EnrollAsync(id, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/enrollments")]
    public async Task<ActionResult<List<SequenceEnrollmentDto>>> ListEnrollments(
        Guid id,
        CancellationToken cancellationToken)
    {
        return Ok(await _sequenceService.ListEnrollmentsAsync(id, cancellationToken));
    }

    [HttpPost("enrollments/{enrollmentId:guid}/cancel")]
    public async Task<IActionResult> CancelEnrollment(Guid enrollmentId, CancellationToken cancellationToken)
    {
        try
        {
            await _sequenceService.CancelEnrollmentAsync(enrollmentId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
