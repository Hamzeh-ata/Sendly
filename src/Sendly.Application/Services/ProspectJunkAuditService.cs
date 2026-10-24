using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class ProspectJunkAuditService
{
    private readonly ISendlyDbContext _db;

    public ProspectJunkAuditService(ISendlyDbContext db)
    {
        _db = db;
    }

    public async Task<ProspectJunkPreviewDto> PreviewAsync(
        Guid? projectId,
        Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        var query = _db.Prospects.AsNoTracking().AsQueryable();
        if (projectId.HasValue)
        {
            query = query.Where(x => x.ProjectId == projectId.Value);
        }

        if (researchRunId.HasValue)
        {
            query = query.Where(x => x.ResearchRunId == researchRunId.Value);
        }

        var prospects = await query.ToListAsync(cancellationToken);
        var matches = new List<ProspectJunkItemDto>();
        foreach (var prospect in prospects)
        {
            if (prospect.Status is ProspectStatus.Rejected or ProspectStatus.DoNotContact)
            {
                continue;
            }

            if (!ProspectJunkClassifier.IsJunkOrganization(
                    prospect.CompanyName,
                    prospect.SourceUrl ?? prospect.Website,
                    prospect.CompanyDescription))
            {
                continue;
            }

            matches.Add(new ProspectJunkItemDto
            {
                Id = prospect.Id,
                CompanyName = prospect.CompanyName,
                SourceUrl = prospect.SourceUrl,
                Website = prospect.Website,
                ResearchRunId = prospect.ResearchRunId,
                Status = prospect.Status.ToString(),
                Reason = "Navigation, legal, marketplace, or generic chrome text."
            });
        }

        return new ProspectJunkPreviewDto
        {
            Count = matches.Count,
            Criteria = "Organization names that match navigation, legal headings, marketplace copy, parking domains, or generic chrome.",
            Reversible = true,
            Items = matches
        };
    }

    public async Task<int> QuarantineAsync(
        IReadOnlyList<Guid> prospectIds,
        CancellationToken cancellationToken)
    {
        if (prospectIds.Count == 0)
        {
            return 0;
        }

        var prospects = await _db.Prospects
            .Where(x => prospectIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var count = 0;
        foreach (var prospect in prospects)
        {
            if (!ProspectJunkClassifier.IsJunkOrganization(
                    prospect.CompanyName,
                    prospect.SourceUrl ?? prospect.Website,
                    prospect.CompanyDescription))
            {
                continue;
            }

            prospect.Status = ProspectStatus.Rejected;
            prospect.IsQualified = false;
            prospect.ResearchNotes = AppendNote(
                prospect.ResearchNotes,
                "Quarantined as navigation/legal/marketplace copy. Reversible by resetting status.");
            prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
            count++;
        }

        if (count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return count;
    }

    private static string AppendNote(string? existing, string note)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return note;
        }

        if (existing.Contains(note, StringComparison.Ordinal))
        {
            return existing;
        }

        return existing + " " + note;
    }
}
