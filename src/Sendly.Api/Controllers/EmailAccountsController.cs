using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/email-accounts")]
public class EmailAccountsController : ControllerBase
{
    private readonly EmailAccountService _emailAccountService;
    private readonly IMicrosoftOAuthService _microsoftOAuth;
    private readonly MicrosoftGraphOptions _graphOptions;

    public EmailAccountsController(
        EmailAccountService emailAccountService,
        IMicrosoftOAuthService microsoftOAuth,
        IOptions<MicrosoftGraphOptions> graphOptions)
    {
        _emailAccountService = emailAccountService;
        _microsoftOAuth = microsoftOAuth;
        _graphOptions = graphOptions.Value;
    }

    [HttpGet]
    public async Task<ActionResult<List<EmailAccountDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _emailAccountService.ListAsync(cancellationToken));
    }

    [HttpGet("status")]
    public async Task<ActionResult<EmailAccountStatusDto>> Status(CancellationToken cancellationToken)
    {
        return Ok(await _emailAccountService.GetStatusAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmailAccountDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var account = await _emailAccountService.GetAsync(id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        return Ok(account);
    }

    [HttpPost("smtp")]
    public async Task<ActionResult<EmailAccountDto>> CreateSmtp(
        [FromBody] UpsertSmtpAccountDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _emailAccountService.CreateSmtpAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("smtp/{id:guid}")]
    public async Task<ActionResult<EmailAccountDto>> UpdateSmtp(
        Guid id,
        [FromBody] UpsertSmtpAccountDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _emailAccountService.UpdateSmtpAsync(id, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _emailAccountService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/set-default")]
    public async Task<ActionResult<EmailAccountDto>> SetDefault(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _emailAccountService.SetDefaultAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<EmailTestResultDto>> Test(
        Guid id,
        [FromBody] SmtpAccountTestRequestDto? request,
        CancellationToken cancellationToken)
    {
        var result = await _emailAccountService.TestAsync(id, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("smtp/test")]
    public async Task<ActionResult<EmailTestResultDto>> TestSmtpDraft(
        [FromBody] SmtpAccountTestRequestDto? request,
        CancellationToken cancellationToken)
    {
        var result = await _emailAccountService.TestAsync(null, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("microsoft/connect")]
    public ActionResult<MicrosoftConnectDto> ConnectMicrosoft()
    {
        try
        {
            return Ok(_emailAccountService.BeginMicrosoftConnect());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("microsoft/callback")]
    public async Task<IActionResult> MicrosoftCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code) || !_microsoftOAuth.ValidateState(state))
        {
            return Redirect(AppendQuery(_graphOptions.FrontendErrorUri, "reason", error ?? "invalid_state"));
        }

        var result = await _microsoftOAuth.CompleteAsync(code, cancellationToken);
        if (!result.Success)
        {
            return Redirect(AppendQuery(_graphOptions.FrontendErrorUri, "reason", "exchange_failed"));
        }

        return Redirect(_graphOptions.FrontendSuccessUri);
    }

    [HttpPost("{id:guid}/disconnect")]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _emailAccountService.DisconnectMicrosoftAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static string AppendQuery(string uri, string key, string value)
    {
        var separator = uri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{uri}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }
}
