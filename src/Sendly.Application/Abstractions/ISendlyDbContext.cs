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

    DbSet<OutreachProjectCity> OutreachProjectCities { get; }

    DbSet<OutreachProjectIndustry> OutreachProjectIndustries { get; }

    DbSet<OutreachProjectTargetRole> OutreachProjectTargetRoles { get; }

    DbSet<ProjectTemplate> ProjectTemplates { get; }

    DbSet<Prospect> Prospects { get; }

    DbSet<ResearchRun> ResearchRuns { get; }

    DbSet<MapCoverageRegion> MapCoverageRegions { get; }

    DbSet<ResearchQuery> ResearchQueries { get; }

    DbSet<ResearchQueryVersion> ResearchQueryVersions { get; }

    DbSet<ResearchSeedUrl> ResearchSeedUrls { get; }

    DbSet<DiscoveredUrl> DiscoveredUrls { get; }

    DbSet<CrawledPage> CrawledPages { get; }

    DbSet<ResearchEvidence> ResearchEvidenceItems { get; }

    DbSet<InvestigationContact> InvestigationContacts { get; }

    DbSet<QualificationCriterionResult> QualificationCriterionResults { get; }

    DbSet<ResearchRunEvent> ResearchRunEvents { get; }

    DbSet<DiscoverySource> DiscoverySources { get; }

    DbSet<OutreachContactHistory> OutreachContactHistories { get; }

    DbSet<OutreachSequence> OutreachSequences { get; }

    DbSet<OutreachSequenceStep> OutreachSequenceSteps { get; }

    DbSet<ProspectSequenceEnrollment> ProspectSequenceEnrollments { get; }

    DbSet<BackgroundJobWorkItem> BackgroundJobWorkItems { get; }

    DbSet<AppConfigurationEntry> AppConfigurationEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
