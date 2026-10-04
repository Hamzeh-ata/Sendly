using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/smtp")]
public class SmtpController : ControllerBase
{
    private readonly SmtpSettingsService _smtpSettingsService;

    public SmtpController(SmtpSettingsService smtpSettingsService)
    {
        _smtpSettingsService = smtpSettingsService;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<SmtpSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await _smtpSettingsService.GetAsync(cancellationToken);
        return Ok(settings);
    }

    [HttpPut("settings")]
    public async Task<ActionResult<SmtpSettingsDto>> UpsertSettings(
        [FromBody] UpsertSmtpSettingsDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _smtpSettingsService.UpsertAsync(request, cancellationToken);
            return Ok(settings);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("settings")]
    public async Task<IActionResult> DeleteSettings(CancellationToken cancellationToken)
    {
        await _smtpSettingsService.DeleteAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("test-connection")]
    public async Task<ActionResult<SmtpTestResultDto>> TestConnection(
        [FromBody] UpsertSmtpSettingsDto? request,
        CancellationToken cancellationToken)
    {
        var result = await _smtpSettingsService.TestConnectionAsync(request, cancellationToken);
        return Ok(result);
    }
}
