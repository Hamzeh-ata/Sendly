using Sendly.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Abstractions;

public interface ISendlyDbContext
{
    DbSet<EmailAccount> EmailAccounts { get; }

    DbSet<Campaign> Campaigns { get; }

    DbSet<CampaignRecipient> CampaignRecipients { get; }

    DbSet<CampaignAttachment> CampaignAttachments { get; }

    DbSet<EmailSignature> EmailSignatures { get; }

    DbSet<SignatureSettings> SignatureSettings { get; }

    DbSet<EmailTemplate> EmailTemplates { get; }

    DbSet<TemplateRecipientHistory> TemplateRecipientHistories { get; }

    DbSet<OutreachProject> OutreachProjects { get; }

    DbSet<OutreachProjectKeyword> OutreachProjectKeywords { get; }

    DbSet<OutreachProjectCountry> OutreachProjectCountries { get; }

    DbSet<OutreachProjectIndustry> OutreachProjectIndustries { get; }

    DbSet<OutreachProjectTargetRole> OutreachProjectTargetRoles { get; }

    DbSet<ProjectTemplate> ProjectTemplates { get; }

    DbSet<Prospect> Prospects { get; }

    DbSet<ResearchRun> ResearchRuns { get; }

    DbSet<OutreachContactHistory> OutreachContactHistories { get; }

    DbSet<OutreachSequence> OutreachSequences { get; }

    DbSet<OutreachSequenceStep> OutreachSequenceSteps { get; }

    DbSet<ProspectSequenceEnrollment> ProspectSequenceEnrollments { get; }

    DbSet<BackgroundJobWorkItem> BackgroundJobWorkItems { get; }

    DbSet<AppConfigurationEntry> AppConfigurationEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
