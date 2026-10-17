using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/app-settings")]
public class AppSettingsController : ControllerBase
{
    private readonly AppSettingsService _appSettingsService;
    private readonly IBraveSearchClient _braveSearchClient;
    private readonly ISearXngSearchClient _searXngSearchClient;

    public AppSettingsController(
        AppSettingsService appSettingsService,
        IBraveSearchClient braveSearchClient,
        ISearXngSearchClient searXngSearchClient)
    {
        _appSettingsService = appSettingsService;
        _braveSearchClient = braveSearchClient;
        _searXngSearchClient = searXngSearchClient;
    }

    [HttpGet]
    public ActionResult<AppSettingsDto> Get()
    {
        return Ok(_appSettingsService.Get());
    }

    [HttpPut]
    public async Task<ActionResult<AppSettingsDto>> Update(
        [FromBody] AppSettingsDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _appSettingsService.UpdateAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("brave-search/test")]
    public async Task<ActionResult<ProviderConnectionTestResult>> TestBraveSearch(CancellationToken cancellationToken)
    {
        var result = await _braveSearchClient.TestConnectionAsync(cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("searxng/test")]
    public async Task<ActionResult<ProviderConnectionTestResult>> TestSearXng(CancellationToken cancellationToken)
    {
        var result = await _searXngSearchClient.TestConnectionAsync(cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
