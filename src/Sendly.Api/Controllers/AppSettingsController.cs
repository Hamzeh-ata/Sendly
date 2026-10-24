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
    private readonly ILocalAiHealthService _localAiHealth;

    public AppSettingsController(
        AppSettingsService appSettingsService,
        IBraveSearchClient braveSearchClient,
        ILocalAiHealthService localAiHealth)
    {
        _appSettingsService = appSettingsService;
        _braveSearchClient = braveSearchClient;
        _localAiHealth = localAiHealth;
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
    public ActionResult<ProviderConnectionTestResult> TestSearXng()
    {
        return BadRequest(new ProviderConnectionTestResult
        {
            Success = false,
            Status = "Disabled",
            Message = "SearXNG is disabled. Sendly uses zero-budget public HTML search, directories, Common Crawl and prior research instead."
        });
    }

    [HttpPost("ollama/test")]
    public async Task<ActionResult<LocalAiHealthResult>> TestOllama(CancellationToken cancellationToken)
    {
        var result = await _localAiHealth.CheckHealthAsync(cancellationToken);
        if (!result.IsReachable)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
