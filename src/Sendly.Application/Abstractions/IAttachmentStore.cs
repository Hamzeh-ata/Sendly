using Sendly.Application.DTOs;

namespace Sendly.Application.Abstractions;

public interface IAttachmentStore
{
    Task<AttachmentUploadResultDto> SaveTemporaryAsync(
        Stream content,
        string fileName,
        string contentType,
        long sizeBytes,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EmailAttachmentContent>> LoadAsync(
        IEnumerable<Guid> attachmentIds,
        CancellationToken cancellationToken);

    Task AssignToCampaignAsync(IEnumerable<Guid> attachmentIds, Guid campaignId, CancellationToken cancellationToken);

    Task AssignToTemplateAsync(IEnumerable<Guid> attachmentIds, Guid templateId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken);

    Task CleanupOrphansAsync(CancellationToken cancellationToken);
}
