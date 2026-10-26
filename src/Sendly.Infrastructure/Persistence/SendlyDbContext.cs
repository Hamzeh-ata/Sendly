using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Infrastructure.Persistence;

public class SendlyDbContext : DbContext, ISendlyDbContext
{
    public SendlyDbContext(DbContextOptions<SendlyDbContext> options) : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Guid>().HaveConversion<UppercaseGuidConverter>();
        configurationBuilder.Properties<Guid?>().HaveConversion<UppercaseNullableGuidConverter>();
    }

    public DbSet<EmailAccount> EmailAccounts => Set<EmailAccount>();

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();

    public DbSet<CampaignAttachment> CampaignAttachments => Set<CampaignAttachment>();

    public DbSet<EmailSignature> EmailSignatures => Set<EmailSignature>();

    public DbSet<SignatureSettings> SignatureSettings => Set<SignatureSettings>();

    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    public DbSet<TemplateRecipientHistory> TemplateRecipientHistories => Set<TemplateRecipientHistory>();

    public DbSet<OutreachProject> OutreachProjects => Set<OutreachProject>();

    public DbSet<OutreachProjectKeyword> OutreachProjectKeywords => Set<OutreachProjectKeyword>();

    public DbSet<OutreachProjectCountry> OutreachProjectCountries => Set<OutreachProjectCountry>();

    public DbSet<OutreachProjectCity> OutreachProjectCities => Set<OutreachProjectCity>();

    public DbSet<OutreachProjectIndustry> OutreachProjectIndustries => Set<OutreachProjectIndustry>();

    public DbSet<OutreachProjectTargetRole> OutreachProjectTargetRoles => Set<OutreachProjectTargetRole>();

    public DbSet<ProjectTemplate> ProjectTemplates => Set<ProjectTemplate>();

    public DbSet<Prospect> Prospects => Set<Prospect>();

    public DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();

    public DbSet<MapCoverageRegion> MapCoverageRegions => Set<MapCoverageRegion>();

    public DbSet<ResearchQuery> ResearchQueries => Set<ResearchQuery>();

    public DbSet<ResearchQueryVersion> ResearchQueryVersions => Set<ResearchQueryVersion>();

    public DbSet<ResearchSeedUrl> ResearchSeedUrls => Set<ResearchSeedUrl>();

    public DbSet<DiscoveredUrl> DiscoveredUrls => Set<DiscoveredUrl>();

    public DbSet<CrawledPage> CrawledPages => Set<CrawledPage>();

    public DbSet<ResearchEvidence> ResearchEvidenceItems => Set<ResearchEvidence>();

    public DbSet<InvestigationContact> InvestigationContacts => Set<InvestigationContact>();

    public DbSet<QualificationCriterionResult> QualificationCriterionResults => Set<QualificationCriterionResult>();

    public DbSet<ResearchRunEvent> ResearchRunEvents => Set<ResearchRunEvent>();

    public DbSet<DiscoverySource> DiscoverySources => Set<DiscoverySource>();

    public DbSet<OutreachContactHistory> OutreachContactHistories => Set<OutreachContactHistory>();

    public DbSet<OutreachSequence> OutreachSequences => Set<OutreachSequence>();

    public DbSet<OutreachSequenceStep> OutreachSequenceSteps => Set<OutreachSequenceStep>();

    public DbSet<ProspectSequenceEnrollment> ProspectSequenceEnrollments => Set<ProspectSequenceEnrollment>();

    public DbSet<BackgroundJobWorkItem> BackgroundJobWorkItems => Set<BackgroundJobWorkItem>();

    public DbSet<AppConfigurationEntry> AppConfigurationEntries => Set<AppConfigurationEntry>();
}
