using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/research-runs")]
public class ResearchRunsController : ControllerBase
{
    private readonly ResearchRunService _researchRunService;
    private readonly ResearchDiagnosticsService _diagnosticsService;
    private readonly ProspectJunkAuditService _junkAuditService;

    public ResearchRunsController(
        ResearchRunService researchRunService,
        ResearchDiagnosticsService diagnosticsService,
        ProspectJunkAuditService junkAuditService)
    {
        _researchRunService = researchRunService;
        _diagnosticsService = diagnosticsService;
        _junkAuditService = junkAuditService;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<ResearchOverviewDto>> Overview(CancellationToken cancellationToken)
    {
        return Ok(await _diagnosticsService.GetOverviewAsync(cancellationToken));
    }

    [HttpGet("errors")]
    public async Task<ActionResult<List<ResearchErrorGroupDto>>> ListErrors(
        [FromQuery] Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        return Ok(await _diagnosticsService.ListErrorsAsync(researchRunId, cancellationToken));
    }

    [HttpGet]
    public async Task<ActionResult<List<ResearchRunSummaryDto>>> List(
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? templateId,
        CancellationToken cancellationToken)
    {
        return Ok(await _researchRunService.ListAsync(projectId, templateId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchRunDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var run = await _researchRunService.GetAsync(id, cancellationToken);
        if (run is null)
        {
            return NotFound(new { message = "Research run was not found." });
        }

        return Ok(run);
    }

    [HttpGet("{id:guid}/discovered-urls")]
    public async Task<ActionResult<List<DiscoveredUrlDto>>> ListDiscoveredUrls(
        Guid id,
        CancellationToken cancellationToken)
    {
        var run = await _researchRunService.GetAsync(id, cancellationToken);
        if (run is null)
        {
            return NotFound(new { message = "Research run was not found." });
        }

        return Ok(await _diagnosticsService.ListDiscoveredUrlsAsync(id, cancellationToken));
    }

    [HttpGet("{id:guid}/crawled-pages")]
    public async Task<ActionResult<List<CrawledPageDto>>> ListCrawledPages(
        Guid id,
        CancellationToken cancellationToken)
    {
        var run = await _researchRunService.GetAsync(id, cancellationToken);
        if (run is null)
        {
            return NotFound(new { message = "Research run was not found." });
        }

        return Ok(await _diagnosticsService.ListCrawledPagesAsync(id, cancellationToken));
    }

    [HttpGet("{id:guid}/crawled-pages/{pageId:guid}")]
    public async Task<ActionResult<CrawledPageDto>> GetCrawledPage(
        Guid id,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        var run = await _researchRunService.GetAsync(id, cancellationToken);
        if (run is null)
        {
            return NotFound(new { message = "Research run was not found." });
        }

        var page = await _diagnosticsService.GetCrawledPageAsync(id, pageId, cancellationToken);
        if (page is null)
        {
            return NotFound(new { message = "Crawled page was not found." });
        }

        return Ok(page);
    }

    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<List<ResearchRunEventDto>>> ListEvents(
        Guid id,
        [FromQuery] string? severity,
        [FromQuery] string? stage,
        [FromQuery] string? eventType,
        [FromQuery] string? search,
        [FromQuery] Guid? afterId,
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default)
    {
        var run = await _researchRunService.GetAsync(id, cancellationToken);
        if (run is null)
        {
            return NotFound(new { message = "Research run was not found." });
        }

        return Ok(await _diagnosticsService.ListEventsAsync(
            id,
            cancellationToken,
            severity,
            stage,
            eventType,
            search,
            afterId,
            take));
    }

    [HttpGet("{id:guid}/junk-prospects")]
    public async Task<ActionResult<ProspectJunkPreviewDto>> PreviewJunk(
        Guid id,
        CancellationToken cancellationToken)
    {
        var run = await _researchRunService.GetAsync(id, cancellationToken);
        if (run is null)
        {
            return NotFound(new { message = "Research run was not found." });
        }

        return Ok(await _junkAuditService.PreviewAsync(run.ProjectId, id, cancellationToken));
    }

    [HttpPost("{id:guid}/junk-prospects/quarantine")]
    public async Task<ActionResult<object>> QuarantineJunk(
        Guid id,
        [FromBody] ProspectJunkQuarantineRequestDto request,
        CancellationToken cancellationToken)
    {
        var run = await _researchRunService.GetAsync(id, cancellationToken);
        if (run is null)
        {
            return NotFound(new { message = "Research run was not found." });
        }

        var preview = await _junkAuditService.PreviewAsync(run.ProjectId, id, cancellationToken);
        var allowed = preview.Items.Select(x => x.Id).ToHashSet();
        var selected = request.ProspectIds.Where(allowed.Contains).ToList();
        var count = await _junkAuditService.QuarantineAsync(selected, cancellationToken);
        return Ok(new { quarantined = count, previewed = preview.Count });
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _researchRunService.CancelAsync(id, cancellationToken);
            return NoContent();
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
            await _researchRunService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
