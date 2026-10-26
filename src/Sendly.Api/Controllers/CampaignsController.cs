using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/campaigns")]
public class CampaignsController : ControllerBase
{
    private readonly CampaignService _campaignService;

    public CampaignsController(CampaignService campaignService)
    {
        _campaignService = campaignService;
    }

    [HttpPost("validate")]
    public ActionResult<ValidateRecipientsResponseDto> Validate([FromBody] ValidateRecipientsRequestDto request)
    {
        return Ok(_campaignService.ValidateRecipients(request));
    }

    [HttpPost]
    public async Task<ActionResult<CampaignDetailDto>> Create(
        [FromBody] CreateCampaignRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var campaign = await _campaignService.CreateAndStartAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = campaign.Id }, campaign);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<CampaignSummaryDto>>> List(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var campaigns = await _campaignService.ListAsync(search, cancellationToken);
        return Ok(campaigns);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CampaignDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var campaign = await _campaignService.GetAsync(id, cancellationToken);
            return Ok(campaign);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/recipients")]
    public async Task<ActionResult<List<CampaignRecipientDto>>> GetRecipients(
        Guid id,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var recipients = await _campaignService.GetRecipientsAsync(id, status, cancellationToken);
        return Ok(recipients);
    }

    [HttpGet("{id:guid}/template-recipient-check")]
    public async Task<ActionResult<CheckTemplateRecipientsResponseDto>> CheckTemplateRecipients(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _campaignService.CheckTemplateRecipientsAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/allow-already-contacted")]
    public async Task<ActionResult<CampaignDetailDto>> SetAllowAlreadyContacted(
        Guid id,
        [FromBody] SetCampaignAllowAlreadyContactedRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _campaignService.SetAllowAlreadyContactedAsync(
                id,
                request.AllowAlreadyContacted,
                cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/exclude-already-contacted")]
    public async Task<ActionResult<CampaignDetailDto>> ExcludeAlreadyContacted(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _campaignService.ExcludeAlreadyContactedAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _campaignService.StartAsync(id, cancellationToken);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _campaignService.CancelAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/retry-failed")]
    public async Task<IActionResult> RetryFailed(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _campaignService.RetryFailedAsync(id, cancellationToken);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _campaignService.ExportCsvAsync(id, cancellationToken);
            return File(bytes, "text/csv", $"campaign-{id}.csv");
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
