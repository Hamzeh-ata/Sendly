using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class TemplateRecipientProtectionService
{
    private readonly ISendlyDbContext _db;

    public TemplateRecipientProtectionService(ISendlyDbContext db)
    {
        _db = db;
    }

    public async Task<int> BackfillFromExistingCampaignsAsync(CancellationToken cancellationToken)
    {
        var campaigns = await _db.Campaigns
            .AsNoTracking()
            .Include(x => x.Recipients)
            .Where(x => x.TemplateId != null)
            .ToListAsync(cancellationToken);

        SortCampaignsByCreatedAscending(campaigns);

        var addedOrUpdated = 0;
        foreach (var campaign in campaigns)
        {
            if (campaign.TemplateId is null)
            {
                continue;
            }

            foreach (var recipient in campaign.Recipients)
            {
                if (recipient.Status != RecipientDeliveryStatus.Sent)
                {
                    continue;
                }

                var changed = await UpsertSentHistoryAsync(
                    campaign.TemplateId.Value,
                    campaign.Id,
                    recipient.Email,
                    recipient.LastAttemptAtUtc ?? campaign.CompletedAtUtc ?? campaign.CreatedAtUtc,
                    cancellationToken);

                if (changed)
                {
                    addedOrUpdated++;
                }
            }
        }

        return addedOrUpdated;
    }

    public async Task<CheckTemplateRecipientsResponseDto> CheckRecipientsAsync(
        Guid templateId,
        CheckTemplateRecipientsRequestDto request,
        CancellationToken cancellationToken)
    {
        await EnsureTemplateExistsAsync(templateId, cancellationToken);
        var parsed = EmailAddressParser.Parse(request.RawRecipients);
        var sentMap = await LoadSentMapAsync(templateId, parsed.ValidEmails, cancellationToken);

        var response = new CheckTemplateRecipientsResponseDto();
        foreach (var email in parsed.ValidEmails)
        {
            var normalized = EmailAddressParser.Normalize(email);
            sentMap.TryGetValue(normalized, out var previous);
            var alreadyContacted = previous is not null;
            response.Recipients.Add(new TemplateRecipientCheckItemDto
            {
                Email = email,
                AlreadyContacted = alreadyContacted,
                PreviousSentAtUtc = previous?.SentAtUtc,
                PreviousCampaignId = previous?.CampaignId
            });

            if (alreadyContacted)
            {
                response.AlreadyContactedCount++;
            }
            else
            {
                response.NewCount++;
            }
        }

        response.TotalCount = response.Recipients.Count;
        return response;
    }

    public async Task<List<string>> FilterSendableEmailsAsync(
        Guid templateId,
        IReadOnlyList<string> emails,
        bool allowAlreadyContacted,
        CancellationToken cancellationToken)
    {
        if (allowAlreadyContacted)
        {
            return emails.ToList();
        }

        var sentMap = await LoadSentMapAsync(templateId, emails, cancellationToken);
        var result = new List<string>();
        foreach (var email in emails)
        {
            var normalized = EmailAddressParser.Normalize(email);
            if (!sentMap.ContainsKey(normalized))
            {
                result.Add(email);
            }
        }

        return result;
    }

    public async Task<bool> TryClaimAsync(
        Guid templateId,
        Guid campaignId,
        string recipientEmail,
        bool allowAlreadyContacted,
        CancellationToken cancellationToken)
    {
        var normalized = EmailAddressParser.Normalize(recipientEmail);
        var existing = await _db.TemplateRecipientHistories
            .FirstOrDefaultAsync(
                x => x.TemplateId == templateId && x.NormalizedRecipientEmail == normalized,
                cancellationToken);

        if (existing is null)
        {
            _db.TemplateRecipientHistories.Add(new TemplateRecipientHistory
            {
                TemplateId = templateId,
                CampaignId = campaignId,
                RecipientEmail = recipientEmail.Trim(),
                NormalizedRecipientEmail = normalized,
                Status = TemplateRecipientContactStatus.Claiming,
                AttemptCount = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                return await CanRetryExistingClaimAsync(
                    templateId,
                    campaignId,
                    recipientEmail,
                    normalized,
                    allowAlreadyContacted,
                    cancellationToken);
            }
        }

        if (existing.Status == TemplateRecipientContactStatus.Sent && !allowAlreadyContacted)
        {
            return false;
        }

        if (existing.Status == TemplateRecipientContactStatus.Claiming
            && existing.CampaignId != campaignId)
        {
            return false;
        }

        existing.Status = TemplateRecipientContactStatus.Claiming;
        existing.CampaignId = campaignId;
        existing.RecipientEmail = recipientEmail.Trim();
        existing.AttemptCount += 1;
        existing.FailureReason = null;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task MarkSentAsync(
        MarkTemplateRecipientSentRequest request,
        CancellationToken cancellationToken)
    {
        var history = await FindHistoryAsync(request.TemplateId, request.RecipientEmail, cancellationToken);
        if (history is null)
        {
            _db.TemplateRecipientHistories.Add(new TemplateRecipientHistory
            {
                TemplateId = request.TemplateId,
                CampaignId = request.CampaignId,
                RecipientEmail = request.RecipientEmail.Trim(),
                NormalizedRecipientEmail = EmailAddressParser.Normalize(request.RecipientEmail),
                Status = TemplateRecipientContactStatus.Sent,
                SentAtUtc = DateTimeOffset.UtcNow,
                AttemptCount = 1,
                Provider = request.Provider,
                SendingAccountId = request.SendingAccountId,
                SendingAccountEmail = request.SendingAccountEmail,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        history.Status = TemplateRecipientContactStatus.Sent;
        history.CampaignId = request.CampaignId;
        history.RecipientEmail = request.RecipientEmail.Trim();
        history.SentAtUtc = DateTimeOffset.UtcNow;
        history.FailureReason = null;
        history.Provider = request.Provider;
        history.SendingAccountId = request.SendingAccountId;
        history.SendingAccountEmail = request.SendingAccountEmail;
        history.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid templateId,
        Guid campaignId,
        string recipientEmail,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        var history = await FindHistoryAsync(templateId, recipientEmail, cancellationToken);
        if (history is null)
        {
            _db.TemplateRecipientHistories.Add(new TemplateRecipientHistory
            {
                TemplateId = templateId,
                CampaignId = campaignId,
                RecipientEmail = recipientEmail.Trim(),
                NormalizedRecipientEmail = EmailAddressParser.Normalize(recipientEmail),
                Status = TemplateRecipientContactStatus.Failed,
                FailureReason = failureReason,
                AttemptCount = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (history.Status == TemplateRecipientContactStatus.Sent)
        {
            return;
        }

        history.Status = TemplateRecipientContactStatus.Failed;
        history.CampaignId = campaignId;
        history.RecipientEmail = recipientEmail.Trim();
        history.FailureReason = failureReason;
        history.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseClaimAsync(
        Guid templateId,
        Guid campaignId,
        string recipientEmail,
        CancellationToken cancellationToken)
    {
        var history = await FindHistoryAsync(templateId, recipientEmail, cancellationToken);
        if (history is null)
        {
            return;
        }

        if (history.Status != TemplateRecipientContactStatus.Claiming
            || history.CampaignId != campaignId)
        {
            return;
        }

        _db.TemplateRecipientHistories.Remove(history);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<TemplateRecipientHistoryItemDto>> ListHistoryAsync(
        Guid templateId,
        string? search,
        CancellationToken cancellationToken)
    {
        await EnsureTemplateExistsAsync(templateId, cancellationToken);

        var rows = await _db.TemplateRecipientHistories
            .AsNoTracking()
            .Where(x => x.TemplateId == templateId)
            .ToListAsync(cancellationToken);

        SortHistoryDescending(rows);

        var campaignIds = new HashSet<Guid>();
        foreach (var row in rows)
        {
            campaignIds.Add(row.CampaignId);
        }

        var subjects = await LoadCampaignSubjectsAsync(campaignIds, cancellationToken);
        var result = new List<TemplateRecipientHistoryItemDto>();
        foreach (var row in rows)
        {
            if (!MatchesHistorySearch(row, search))
            {
                continue;
            }

            subjects.TryGetValue(row.CampaignId, out var subject);
            result.Add(new TemplateRecipientHistoryItemDto
            {
                Id = row.Id,
                Email = row.RecipientEmail,
                Status = row.Status,
                SentAtUtc = row.SentAtUtc ?? row.UpdatedAtUtc,
                UpdatedAtUtc = row.UpdatedAtUtc,
                CampaignId = row.CampaignId,
                CampaignSubject = subject,
                FailureReason = row.FailureReason,
                AttemptCount = row.AttemptCount
            });
        }

        return result;
    }

    public async Task<TemplateStatsDto> GetStatsAsync(Guid templateId, CancellationToken cancellationToken)
    {
        await EnsureTemplateExistsAsync(templateId, cancellationToken);

        var histories = await _db.TemplateRecipientHistories
            .AsNoTracking()
            .Where(x => x.TemplateId == templateId)
            .ToListAsync(cancellationToken);

        var campaigns = await _db.Campaigns
            .AsNoTracking()
            .Where(x => x.TemplateId == templateId)
            .ToListAsync(cancellationToken);

        var stats = new TemplateStatsDto
        {
            TotalCampaigns = campaigns.Count,
            UniqueRecipients = histories.Count
        };

        foreach (var history in histories)
        {
            if (history.Status == TemplateRecipientContactStatus.Sent)
            {
                stats.SuccessfullyContacted++;
            }
            else if (history.Status == TemplateRecipientContactStatus.Failed)
            {
                stats.Failed++;
            }
        }

        DateTimeOffset? lastUsed = null;
        foreach (var campaign in campaigns)
        {
            if (lastUsed is null || campaign.CreatedAtUtc > lastUsed)
            {
                lastUsed = campaign.CreatedAtUtc;
            }
        }

        stats.LastUsedAtUtc = lastUsed;
        return stats;
    }

    public async Task<Dictionary<Guid, TemplateStatsDto>> GetStatsMapAsync(
        IEnumerable<Guid> templateIds,
        CancellationToken cancellationToken)
    {
        var ids = templateIds.ToList();
        var map = new Dictionary<Guid, TemplateStatsDto>();
        foreach (var id in ids)
        {
            map[id] = new TemplateStatsDto();
        }

        if (ids.Count == 0)
        {
            return map;
        }

        var histories = await _db.TemplateRecipientHistories
            .AsNoTracking()
            .Where(x => ids.Contains(x.TemplateId))
            .ToListAsync(cancellationToken);

        var campaigns = await _db.Campaigns
            .AsNoTracking()
            .Where(x => x.TemplateId != null && ids.Contains(x.TemplateId.Value))
            .ToListAsync(cancellationToken);

        foreach (var history in histories)
        {
            if (!map.TryGetValue(history.TemplateId, out var stats))
            {
                continue;
            }

            stats.UniqueRecipients++;
            if (history.Status == TemplateRecipientContactStatus.Sent)
            {
                stats.SuccessfullyContacted++;
            }
            else if (history.Status == TemplateRecipientContactStatus.Failed)
            {
                stats.Failed++;
            }
        }

        foreach (var campaign in campaigns)
        {
            if (campaign.TemplateId is null)
            {
                continue;
            }

            if (!map.TryGetValue(campaign.TemplateId.Value, out var stats))
            {
                continue;
            }

            stats.TotalCampaigns++;
            if (stats.LastUsedAtUtc is null || campaign.CreatedAtUtc > stats.LastUsedAtUtc)
            {
                stats.LastUsedAtUtc = campaign.CreatedAtUtc;
            }
        }

        return map;
    }

    private async Task<bool> UpsertSentHistoryAsync(
        Guid templateId,
        Guid campaignId,
        string recipientEmail,
        DateTimeOffset sentAtUtc,
        CancellationToken cancellationToken)
    {
        var normalized = EmailAddressParser.Normalize(recipientEmail);
        var existing = await _db.TemplateRecipientHistories
            .FirstOrDefaultAsync(
                x => x.TemplateId == templateId && x.NormalizedRecipientEmail == normalized,
                cancellationToken);

        if (existing is null)
        {
            _db.TemplateRecipientHistories.Add(new TemplateRecipientHistory
            {
                TemplateId = templateId,
                CampaignId = campaignId,
                RecipientEmail = recipientEmail.Trim(),
                NormalizedRecipientEmail = normalized,
                Status = TemplateRecipientContactStatus.Sent,
                SentAtUtc = sentAtUtc,
                AttemptCount = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (existing.Status == TemplateRecipientContactStatus.Sent)
        {
            return false;
        }

        existing.Status = TemplateRecipientContactStatus.Sent;
        existing.CampaignId = campaignId;
        existing.RecipientEmail = recipientEmail.Trim();
        existing.SentAtUtc = sentAtUtc;
        existing.FailureReason = null;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void SortCampaignsByCreatedAscending(List<Campaign> campaigns)
    {
        campaigns.Sort(static (left, right) => left.CreatedAtUtc.CompareTo(right.CreatedAtUtc));
    }

    private async Task<bool> CanRetryExistingClaimAsync(
        Guid templateId,
        Guid campaignId,
        string recipientEmail,
        string normalized,
        bool allowAlreadyContacted,
        CancellationToken cancellationToken)
    {
        var existing = await _db.TemplateRecipientHistories
            .FirstOrDefaultAsync(
                x => x.TemplateId == templateId && x.NormalizedRecipientEmail == normalized,
                cancellationToken);

        if (existing is null)
        {
            return false;
        }

        if (existing.Status == TemplateRecipientContactStatus.Sent && !allowAlreadyContacted)
        {
            return false;
        }

        if (existing.Status == TemplateRecipientContactStatus.Claiming
            && existing.CampaignId != campaignId)
        {
            return false;
        }

        existing.Status = TemplateRecipientContactStatus.Claiming;
        existing.CampaignId = campaignId;
        existing.RecipientEmail = recipientEmail.Trim();
        existing.AttemptCount += 1;
        existing.FailureReason = null;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Dictionary<string, TemplateRecipientHistory>> LoadSentMapAsync(
        Guid templateId,
        IReadOnlyList<string> emails,
        CancellationToken cancellationToken)
    {
        var normalized = new List<string>();
        foreach (var email in emails)
        {
            normalized.Add(EmailAddressParser.Normalize(email));
        }

        if (normalized.Count == 0)
        {
            return new Dictionary<string, TemplateRecipientHistory>(StringComparer.Ordinal);
        }

        var rows = await _db.TemplateRecipientHistories
            .AsNoTracking()
            .Where(x =>
                x.TemplateId == templateId
                && x.Status == TemplateRecipientContactStatus.Sent
                && normalized.Contains(x.NormalizedRecipientEmail))
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, TemplateRecipientHistory>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            map[row.NormalizedRecipientEmail] = row;
        }

        return map;
    }

    private async Task<TemplateRecipientHistory?> FindHistoryAsync(
        Guid templateId,
        string recipientEmail,
        CancellationToken cancellationToken)
    {
        var normalized = EmailAddressParser.Normalize(recipientEmail);
        return await _db.TemplateRecipientHistories
            .FirstOrDefaultAsync(
                x => x.TemplateId == templateId && x.NormalizedRecipientEmail == normalized,
                cancellationToken);
    }

    private async Task EnsureTemplateExistsAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var exists = await _db.EmailTemplates
            .AsNoTracking()
            .AnyAsync(x => x.Id == templateId, cancellationToken);

        if (!exists)
        {
            throw new InvalidOperationException("Template was not found.");
        }
    }

    private async Task<Dictionary<Guid, string>> LoadCampaignSubjectsAsync(
        HashSet<Guid> campaignIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();
        if (campaignIds.Count == 0)
        {
            return result;
        }

        var campaigns = await _db.Campaigns
            .AsNoTracking()
            .Where(x => campaignIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var campaign in campaigns)
        {
            result[campaign.Id] = campaign.Subject;
        }

        return result;
    }

    private static void SortHistoryDescending(List<TemplateRecipientHistory> rows)
    {
        rows.Sort(static (left, right) => right.UpdatedAtUtc.CompareTo(left.UpdatedAtUtc));
    }

    private static bool MatchesHistorySearch(TemplateRecipientHistory row, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return row.RecipientEmail.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.NormalizedRecipientEmail.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
