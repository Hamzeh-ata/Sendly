using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/templates")]
public class TemplatesController : ControllerBase
{
    private readonly EmailTemplateService _templateService;
    private readonly TemplateRecipientProtectionService _recipientProtection;

    public TemplatesController(
        EmailTemplateService templateService,
        TemplateRecipientProtectionService recipientProtection)
    {
        _templateService = templateService;
        _recipientProtection = recipientProtection;
    }

    [HttpGet]
    public async Task<ActionResult<List<EmailTemplateSummaryDto>>> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        return Ok(await _templateService.ListAsync(search, status, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmailTemplateDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var template = await _templateService.GetAsync(id, cancellationToken);
        if (template is null)
        {
            return NotFound(new { message = "Template was not found." });
        }

        return Ok(template);
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<ActionResult<EmailTemplatePreviewDto>> Preview(Guid id, CancellationToken cancellationToken)
    {
        var preview = await _templateService.PreviewAsync(id, cancellationToken);
        if (preview is null)
        {
            return NotFound(new { message = "Template was not found." });
        }

        return Ok(preview);
    }

    [HttpPost]
    public async Task<ActionResult<EmailTemplateDetailDto>> Create(
        [FromBody] UpsertEmailTemplateDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var template = await _templateService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmailTemplateDetailDto>> Update(
        Guid id,
        [FromBody] UpsertEmailTemplateDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _templateService.UpdateAsync(id, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _templateService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<EmailTemplateDetailDto>> Duplicate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _templateService.DuplicateAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/check-recipients")]
    public async Task<ActionResult<CheckTemplateRecipientsResponseDto>> CheckRecipients(
        Guid id,
        [FromBody] CheckTemplateRecipientsRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _recipientProtection.CheckRecipientsAsync(id, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/recipient-history")]
    public async Task<ActionResult<List<TemplateRecipientHistoryItemDto>>> RecipientHistory(
        Guid id,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _recipientProtection.ListHistoryAsync(id, search, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/stats")]
    public async Task<ActionResult<TemplateStatsDto>> Stats(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _recipientProtection.GetStatsAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
