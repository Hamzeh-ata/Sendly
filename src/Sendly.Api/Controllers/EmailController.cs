using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/email")]
public class EmailController : ControllerBase
{
    private readonly CampaignService _campaignService;

    public EmailController(CampaignService campaignService)
    {
        _campaignService = campaignService;
    }

    [HttpPost("test")]
    public async Task<ActionResult<EmailTestResultDto>> SendTest(
        [FromBody] TestEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _campaignService.SendTestEmailAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
