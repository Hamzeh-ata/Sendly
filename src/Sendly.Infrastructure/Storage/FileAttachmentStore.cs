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
            TemplateId = null,
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
        var ids = ToIdList(attachmentIds);
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
        var ids = ToIdList(attachmentIds);
        if (ids.Count == 0)
        {
            return;
        }

        var entities = await _db.CampaignAttachments
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        EnsureTotalSize(entities);

        foreach (var entity in entities)
        {
            if (entity.CampaignId is null && entity.TemplateId is null)
            {
                entity.CampaignId = campaignId;
                continue;
            }

            _db.CampaignAttachments.Add(CreateSharedCopy(entity, campaignId, null));
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignToTemplateAsync(
        IEnumerable<Guid> attachmentIds,
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var ids = ToIdList(attachmentIds);
        if (ids.Count == 0)
        {
            return;
        }

        var entities = await _db.CampaignAttachments
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        EnsureTotalSize(entities);

        foreach (var entity in entities)
        {
            if (entity.CampaignId is null && entity.TemplateId is null)
            {
                entity.TemplateId = templateId;
                continue;
            }

            if (entity.TemplateId == templateId)
            {
                continue;
            }

            _db.CampaignAttachments.Add(CreateSharedCopy(entity, null, templateId));
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

        var storedPath = entity.StoredPath;
        _db.CampaignAttachments.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);

        var stillReferenced = await _db.CampaignAttachments
            .AsNoTracking()
            .AnyAsync(x => x.StoredPath == storedPath, cancellationToken);

        if (!stillReferenced && File.Exists(storedPath))
        {
            File.Delete(storedPath);
        }
    }

    public async Task CleanupOrphansAsync(CancellationToken cancellationToken)
    {
        var orphans = await _db.CampaignAttachments
            .Where(x => x.CampaignId == null && x.TemplateId == null)
            .ToListAsync(cancellationToken);

        foreach (var orphan in orphans)
        {
            var storedPath = orphan.StoredPath;
            _db.CampaignAttachments.Remove(orphan);
            await _db.SaveChangesAsync(cancellationToken);

            var stillReferenced = await _db.CampaignAttachments
                .AsNoTracking()
                .AnyAsync(x => x.StoredPath == storedPath, cancellationToken);

            if (!stillReferenced && File.Exists(storedPath))
            {
                File.Delete(storedPath);
            }
        }
    }

    private void EnsureTotalSize(List<CampaignAttachment> entities)
    {
        var totalSize = 0L;
        foreach (var entity in entities)
        {
            totalSize += entity.SizeBytes;
        }

        if (totalSize > _options.MaxTotalSizeBytes)
        {
            throw new InvalidOperationException($"Combined attachment size exceeds {_options.MaxTotalSizeBytes} bytes.");
        }
    }

    private static CampaignAttachment CreateSharedCopy(
        CampaignAttachment source,
        Guid? campaignId,
        Guid? templateId)
    {
        return new CampaignAttachment
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            TemplateId = templateId,
            FileName = source.FileName,
            ContentType = source.ContentType,
            SizeBytes = source.SizeBytes,
            StoredPath = source.StoredPath
        };
    }

    private static List<Guid> ToIdList(IEnumerable<Guid> attachmentIds)
    {
        var ids = new List<Guid>();
        foreach (var id in attachmentIds)
        {
            ids.Add(id);
        }

        return ids;
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
