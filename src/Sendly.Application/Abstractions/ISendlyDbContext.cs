using Sendly.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Abstractions;

public interface ISendlyDbContext
{
    DbSet<SmtpSettings> SmtpSettings { get; }

    DbSet<Campaign> Campaigns { get; }

    DbSet<CampaignRecipient> CampaignRecipients { get; }

    DbSet<CampaignAttachment> CampaignAttachments { get; }

    DbSet<EmailSignature> EmailSignatures { get; }

    DbSet<SignatureSettings> SignatureSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
