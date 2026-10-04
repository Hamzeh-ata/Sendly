using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Infrastructure.Persistence;

public class SendlyDbContext : DbContext, ISendlyDbContext
{
    public SendlyDbContext(DbContextOptions<SendlyDbContext> options) : base(options)
    {
    }

    public DbSet<SmtpSettings> SmtpSettings => Set<SmtpSettings>();

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();

    public DbSet<CampaignAttachment> CampaignAttachments => Set<CampaignAttachment>();

    public DbSet<EmailSignature> EmailSignatures => Set<EmailSignature>();

    public DbSet<SignatureSettings> SignatureSettings => Set<SignatureSettings>();
}
