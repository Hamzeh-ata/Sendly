using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace Sendly.Api.Controllers;

[ApiController]
[Route("api/attachments")]
public class AttachmentsController : ControllerBase
{
    private readonly IAttachmentStore _attachmentStore;

    public AttachmentsController(IAttachmentStore attachmentStore)
    {
        _attachmentStore = attachmentStore;
    }

    [HttpPost]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<AttachmentUploadResultDto>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "A file is required." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _attachmentStore.SaveTemporaryAsync(
                stream,
                file.FileName,
                file.ContentType,
                file.Length,
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _attachmentStore.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
