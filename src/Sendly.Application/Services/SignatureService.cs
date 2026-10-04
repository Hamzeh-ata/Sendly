using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class SignatureService
{
    private readonly ISendlyDbContext _db;

    public SignatureService(ISendlyDbContext db)
    {
        _db = db;
    }

    public async Task<List<EmailSignatureDto>> ListAsync(CancellationToken cancellationToken)
    {
        var entities = await _db.EmailSignatures.AsNoTracking().ToListAsync(cancellationToken);
        SortByName(entities);
        var result = new List<EmailSignatureDto>();
        foreach (var entity in entities)
        {
            result.Add(Map(entity));
        }

        return result;
    }

    public async Task<EmailSignatureDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.EmailSignatures.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<EmailSignatureDto?> GetDefaultAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        if (!settings.SignaturesEnabled)
        {
            return null;
        }

        var entity = await _db.EmailSignatures
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsDefault && x.IsActive, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<EmailSignatureDto> CreateAsync(UpsertEmailSignatureDto request, CancellationToken cancellationToken)
    {
        Validate(request);
        var entity = new EmailSignature
        {
            Name = request.Name.Trim(),
            HtmlContent = request.HtmlContent.Trim(),
            IsActive = request.IsActive,
            IsDefault = request.IsDefault,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        if (entity.IsDefault)
        {
            await ClearDefaultFlagsAsync(cancellationToken);
        }

        _db.EmailSignatures.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<EmailSignatureDto> UpdateAsync(Guid id, UpsertEmailSignatureDto request, CancellationToken cancellationToken)
    {
        Validate(request);
        var entity = await _db.EmailSignatures.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            throw new InvalidOperationException("Signature was not found.");
        }

        if (request.IsDefault)
        {
            await ClearDefaultFlagsAsync(cancellationToken);
        }

        entity.Name = request.Name.Trim();
        entity.HtmlContent = request.HtmlContent.Trim();
        entity.IsActive = request.IsActive;
        entity.IsDefault = request.IsDefault;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.EmailSignatures.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        _db.EmailSignatures.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<EmailSignatureDto> DuplicateAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await _db.EmailSignatures.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (source is null)
        {
            throw new InvalidOperationException("Signature was not found.");
        }

        var copy = new EmailSignature
        {
            Name = BuildDuplicateName(source.Name),
            HtmlContent = source.HtmlContent,
            IsActive = source.IsActive,
            IsDefault = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.EmailSignatures.Add(copy);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(copy);
    }

    public async Task<EmailSignatureDto> SetDefaultAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.EmailSignatures.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            throw new InvalidOperationException("Signature was not found.");
        }

        await ClearDefaultFlagsAsync(cancellationToken);
        entity.IsDefault = true;
        entity.IsActive = true;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<SignatureSettingsDto> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        return new SignatureSettingsDto { SignaturesEnabled = settings.SignaturesEnabled };
    }

    public async Task<SignatureSettingsDto> UpdateSettingsAsync(
        UpdateSignatureSettingsDto request,
        CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.SignaturesEnabled = request.SignaturesEnabled;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return new SignatureSettingsDto { SignaturesEnabled = settings.SignaturesEnabled };
    }

    public async Task<string?> ResolveSignatureHtmlAsync(Guid? signatureId, CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        if (!settings.SignaturesEnabled || !signatureId.HasValue)
        {
            return null;
        }

        var signature = await _db.EmailSignatures
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == signatureId.Value && x.IsActive, cancellationToken);

        return signature?.HtmlContent;
    }

    private async Task ClearDefaultFlagsAsync(CancellationToken cancellationToken)
    {
        var defaults = await _db.EmailSignatures.Where(x => x.IsDefault).ToListAsync(cancellationToken);
        foreach (var item in defaults)
        {
            item.IsDefault = false;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private async Task<SignatureSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _db.SignatureSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new SignatureSettings
        {
            SignaturesEnabled = true,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        _db.SignatureSettings.Add(settings);
        await _db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static void Validate(UpsertEmailSignatureDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Signature name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.HtmlContent))
        {
            throw new InvalidOperationException("Signature content is required.");
        }
    }

    private static string BuildDuplicateName(string name)
    {
        var baseName = name.Trim();
        if (baseName.Length > 180)
        {
            baseName = baseName[..180];
        }

        return $"{baseName} (Copy)";
    }

    private static void SortByName(List<EmailSignature> entities)
    {
        entities.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static EmailSignatureDto Map(EmailSignature entity)
    {
        return new EmailSignatureDto
        {
            Id = entity.Id,
            Name = entity.Name,
            HtmlContent = entity.HtmlContent,
            IsDefault = entity.IsDefault,
            IsActive = entity.IsActive,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        };
    }
}
