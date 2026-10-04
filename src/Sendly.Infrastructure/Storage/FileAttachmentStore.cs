using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Storage;

public class FileAttachmentStore : IAttachmentStore
{
    private readonly ISendlyDbContext _db;
    private readonly AttachmentOptions _options;

    public FileAttachmentStore(ISendlyDbContext db, IOptions<AttachmentOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<AttachmentUploadResultDto> SaveTemporaryAsync(
        Stream content,
        string fileName,
        string contentType,
        long sizeBytes,
        CancellationToken cancellationToken)
    {
        ValidateUpload(fileName, contentType, sizeBytes);
        EnsureStorageRoot();

        var id = Guid.NewGuid();
        var safeName = Path.GetFileName(fileName);
        var storedPath = Path.Combine(_options.StorageRoot, $"{id}_{safeName}");

        await using (var fileStream = File.Create(storedPath))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var entity = new CampaignAttachment
        {
            Id = id,
            CampaignId = null,
            FileName = safeName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StoredPath = storedPath
        };

        _db.CampaignAttachments.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new AttachmentUploadResultDto
        {
            Id = entity.Id,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            SizeBytes = entity.SizeBytes
        };
    }

    public async Task<IReadOnlyList<EmailAttachmentContent>> LoadAsync(
        IEnumerable<Guid> attachmentIds,
        CancellationToken cancellationToken)
    {
        var ids = attachmentIds.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var entities = await _db.CampaignAttachments
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var result = new List<EmailAttachmentContent>();
        foreach (var entity in entities)
        {
            if (!File.Exists(entity.StoredPath))
            {
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(entity.StoredPath, cancellationToken);
            result.Add(new EmailAttachmentContent
            {
                FileName = entity.FileName,
                ContentType = entity.ContentType,
                Content = bytes
            });
        }

        return result;
    }

    public async Task AssignToCampaignAsync(
        IEnumerable<Guid> attachmentIds,
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        var ids = attachmentIds.ToList();
        var entities = await _db.CampaignAttachments
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var totalSize = 0L;
        foreach (var entity in entities)
        {
            totalSize += entity.SizeBytes;
        }

        if (totalSize > _options.MaxTotalSizeBytes)
        {
            throw new InvalidOperationException($"Combined attachment size exceeds {_options.MaxTotalSizeBytes} bytes.");
        }

        foreach (var entity in entities)
        {
            entity.CampaignId = campaignId;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        var entity = await _db.CampaignAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        if (File.Exists(entity.StoredPath))
        {
            File.Delete(entity.StoredPath);
        }

        _db.CampaignAttachments.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task CleanupOrphansAsync(CancellationToken cancellationToken)
    {
        var orphans = await _db.CampaignAttachments
            .Where(x => x.CampaignId == null)
            .ToListAsync(cancellationToken);

        foreach (var orphan in orphans)
        {
            if (File.Exists(orphan.StoredPath))
            {
                File.Delete(orphan.StoredPath);
            }

            _db.CampaignAttachments.Remove(orphan);
        }

        if (orphans.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private void ValidateUpload(string fileName, string contentType, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Attachment file name is required.");
        }

        if (sizeBytes <= 0)
        {
            throw new InvalidOperationException("Attachment is empty.");
        }

        if (sizeBytes > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"Attachment exceeds the maximum size of {_options.MaxFileSizeBytes} bytes.");
        }

        if (!_options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Attachment content type is not allowed.");
        }
    }

    private void EnsureStorageRoot()
    {
        if (!Directory.Exists(_options.StorageRoot))
        {
            Directory.CreateDirectory(_options.StorageRoot);
        }
    }
}
