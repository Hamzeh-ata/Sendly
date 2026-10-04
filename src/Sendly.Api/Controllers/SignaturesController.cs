using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/signatures")]
public class SignaturesController : ControllerBase
{
    private readonly SignatureService _signatureService;

    public SignaturesController(SignatureService signatureService)
    {
        _signatureService = signatureService;
    }

    [HttpGet]
    public async Task<ActionResult<List<EmailSignatureDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _signatureService.ListAsync(cancellationToken));
    }

    [HttpGet("settings")]
    public async Task<ActionResult<SignatureSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        return Ok(await _signatureService.GetSettingsAsync(cancellationToken));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<SignatureSettingsDto>> UpdateSettings(
        [FromBody] UpdateSignatureSettingsDto request,
        CancellationToken cancellationToken)
    {
        return Ok(await _signatureService.UpdateSettingsAsync(request, cancellationToken));
    }

    [HttpGet("default")]
    public async Task<ActionResult<EmailSignatureDto>> GetDefault(CancellationToken cancellationToken)
    {
        var signature = await _signatureService.GetDefaultAsync(cancellationToken);
        if (signature is null)
        {
            return NoContent();
        }

        return Ok(signature);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmailSignatureDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var signature = await _signatureService.GetAsync(id, cancellationToken);
        if (signature is null)
        {
            return NotFound(new { message = "Signature was not found." });
        }

        return Ok(signature);
    }

    [HttpPost]
    public async Task<ActionResult<EmailSignatureDto>> Create(
        [FromBody] UpsertEmailSignatureDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var signature = await _signatureService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = signature.Id }, signature);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmailSignatureDto>> Update(
        Guid id,
        [FromBody] UpsertEmailSignatureDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _signatureService.UpdateAsync(id, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _signatureService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<EmailSignatureDto>> Duplicate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _signatureService.DuplicateAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/set-default")]
    public async Task<ActionResult<EmailSignatureDto>> SetDefault(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _signatureService.SetDefaultAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
