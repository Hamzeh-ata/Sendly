using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Application.Tests.Fakes;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Sendly.Infrastructure.Persistence;
using Sendly.Infrastructure.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Sendly.Application.Tests;

public class DeepContactExtractionAcceptanceTests
{
    [Fact]
    public async Task Directory_listing_yields_multiple_orgs_contact_investigations_and_attributed_contacts()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        MapDirectoryFixture(fetcher);

        var crawler = CreateCrawler(db, fetcher, CreateSeeds());
        var state = new ResearchDiscoveryState();
        var context = CreateContext(project, run, state);

        var discoveredCandidates = new List<ProspectCandidate>();
        for (var i = 0; i < 5; i++)
        {
            var batch = await crawler.DiscoverAsync(context, CancellationToken.None);
            discoveredCandidates.AddRange(batch);
        }

        var schools = discoveredCandidates
            .Where(x =>
                x.CompanyName.Contains("Riverside Prep", StringComparison.OrdinalIgnoreCase)
                || x.CompanyName.Contains("Harbor Independent", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.CompanyName, StringComparer.OrdinalIgnoreCase)
            .Select(MergeCandidates)
            .ToList();

        Assert.True(schools.Count >= 2, $"Expected >=2 schools, got {schools.Count}");
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("riversideprep.edu", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("harborindependent.edu", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("/contact", StringComparison.OrdinalIgnoreCase));

        var processor = CreateProcessor(db, crawler);
        foreach (var school in schools)
        {
            await processor.ProcessCandidateForTestsAsync(project, run, school, state, CancellationToken.None);
        }

        var prospects = await db.Prospects.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        Assert.True(prospects.Count >= 2, $"Expected >=2 persisted prospects, got {prospects.Count}");

        foreach (var prospect in prospects)
        {
            var emails = ProspectContactStore.ParseAttributed(prospect.AdditionalEmailsJson);
            if (!string.IsNullOrWhiteSpace(prospect.Email))
            {
                emails.Insert(0, new AttributedContact
                {
                    Value = prospect.Email,
                    Channel = "email",
                    SourceUrl = prospect.SourceUrl
                });
            }

            var phones = ProspectContactStore.ParseAttributed(prospect.AdditionalPhonesJson);
            if (!string.IsNullOrWhiteSpace(prospect.Phone))
            {
                phones.Insert(0, new AttributedContact
                {
                    Value = prospect.Phone,
                    Channel = "phone",
                    SourceUrl = prospect.SourceUrl
                });
            }

            var contactCount = ProspectContactStore.CountContacts(
                prospect.AdditionalEmailsJson,
                prospect.AdditionalPhonesJson,
                prospect.Email,
                prospect.Phone,
                prospect.WhatsApp);
            Assert.True(
                contactCount >= 2,
                $"{prospect.CompanyName} expected >=2 contacts, got {contactCount} (email={prospect.Email}, phone={prospect.Phone}, extras={emails.Count}/{phones.Count})");
        }

        var evidence = await db.ResearchEvidenceItems
            .Where(x => x.ResearchRunId == run.Id && x.ProspectId != null)
            .ToListAsync();
        Assert.Contains(evidence, x =>
            string.Equals(x.FactType, "email", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(x.SourceUrl)
            && !string.IsNullOrWhiteSpace(x.FactValue));
        Assert.Contains(evidence, x =>
            string.Equals(x.FactType, "phone", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(x.FactType, "email", StringComparison.OrdinalIgnoreCase)
                && evidence.Count(e => e.FactType == "email") >= 2));

        Assert.True(run.ContactsFound >= 4, $"ContactsFound should reflect multi-contact yield, got {run.ContactsFound}");
        Assert.DoesNotContain(fetcher.RequestedUrls, u => u.Host.StartsWith("sso.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiscoveryCoverage_does_not_stop_after_one_or_two_seeds()
    {
        var eligibility = new SourceEligibilityResolveResult
        {
            EligiblePrimary =
            [
                new SourceEligibilityDecision { SourceName = "NAIS", SourceUrl = "https://www.nais.org/school-directory", State = SourceEligibilityState.Eligible },
                new SourceEligibilityDecision { SourceName = "PSR", SourceUrl = "https://www.privateschoolreview.com/find-schools", State = SourceEligibilityState.Eligible },
                new SourceEligibilityDecision { SourceName = "ISC", SourceUrl = "https://www.isc.co.uk/schools/", State = SourceEligibilityState.Eligible }
            ]
        };

        var result = new AutomaticDiscoveryResult
        {
            Candidates =
            [
                new DiscoveredUrlCandidate
                {
                    Url = "https://www.nais.org/school-directory",
                    SourceName = "NAIS",
                    IsIndustryRelevant = true
                },
                new DiscoveredUrlCandidate
                {
                    Url = "https://www.nais.org/school-directory/school/1",
                    SourceName = "NAIS",
                    IsIndustryRelevant = true
                }
            ],
            ProviderStats =
            [
                new DiscoveryProviderStat { ProviderName = "DirectoryCatalog", Attempted = true, Succeeded = true }
            ]
        };

        var context = new UrlDiscoveryContext
        {
            MinCandidatesBeforeRecovery = 10,
            Plan = new DiscoveryPlan
            {
                EligibleProviderNames = ["DirectoryCatalog", "IndustryAssociation", "PriorResearchUrls"]
            }
        };
        context.ExploredProviderNames.Add("DirectoryCatalog");

        Assert.False(DiscoveryCoverageEvaluator.HasSufficientRelevantCoverage(result, context, eligibility));
        Assert.Equal(1, DiscoveryCoverageEvaluator.CountCoveredRelevantSources(result.Candidates, eligibility));
    }

    private static ProspectCandidate MergeCandidates(IGrouping<string, ProspectCandidate> group)
    {
        var primary = group
            .OrderByDescending(x => string.IsNullOrWhiteSpace(x.Email) ? 0 : 1)
            .ThenByDescending(x => x.AttributedEmails.Count + x.AttributedPhones.Count)
            .ThenByDescending(x => x.NameConfidence)
            .First();

        foreach (var other in group.Where(x => !ReferenceEquals(x, primary)))
        {
            if (string.IsNullOrWhiteSpace(primary.Email) && !string.IsNullOrWhiteSpace(other.Email))
            {
                primary.Email = other.Email;
                primary.ContactAttributionState = other.ContactAttributionState;
                primary.ContactAttributionReasonCode = other.ContactAttributionReasonCode;
                primary.ContactEvidenceUrl = other.ContactEvidenceUrl;
            }

            if (string.IsNullOrWhiteSpace(primary.Phone) && !string.IsNullOrWhiteSpace(other.Phone))
            {
                primary.Phone = other.Phone;
            }

            if (string.IsNullOrWhiteSpace(primary.Website) && !string.IsNullOrWhiteSpace(other.Website))
            {
                primary.Website = other.Website;
                primary.OfficialDomain = other.OfficialDomain;
            }

            foreach (var email in other.AttributedEmails)
            {
                if (primary.AttributedEmails.All(x => !string.Equals(x.Value, email.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    primary.AttributedEmails.Add(email);
                }
            }

            foreach (var phone in other.AttributedPhones)
            {
                if (primary.AttributedPhones.All(x => !string.Equals(x.Value, phone.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    primary.AttributedPhones.Add(phone);
                }
            }

            foreach (var email in other.AdditionalEmails)
            {
                if (!primary.AdditionalEmails.Contains(email, StringComparer.OrdinalIgnoreCase)
                    && !string.Equals(primary.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    primary.AdditionalEmails.Add(email);
                }
            }

            foreach (var phone in other.AdditionalPhones)
            {
                if (!primary.AdditionalPhones.Contains(phone, StringComparer.OrdinalIgnoreCase)
                    && !string.Equals(primary.Phone, phone, StringComparison.OrdinalIgnoreCase))
                {
                    primary.AdditionalPhones.Add(phone);
                }
            }

            foreach (var url in other.AdditionalSourceUrls)
            {
                if (!primary.AdditionalSourceUrls.Contains(url, StringComparer.OrdinalIgnoreCase))
                {
                    primary.AdditionalSourceUrls.Add(url);
                }
            }
        }

        return primary;
    }

    private static void MapDirectoryFixture(FakeSafeHttpFetcher fetcher)
    {
        fetcher.Map(
            "https://www.nais.org/school-directory",
            FakeSafeHttpFetcher.Html(
                "https://www.nais.org/school-directory",
                """
                <html><body>
                  <div class="listing-card">
                    <h2>Riverside Prep Academy</h2>
                    <a href="/school-directory/school/1001">Profile</a>
                    <a href="https://riversideprep.edu">Website</a>
                    <a href="mailto:office@riversideprep.edu">office@riversideprep.edu</a>
                    <span>+1-555-0100</span>
                  </div>
                  <div class="listing-card">
                    <h2>Harbor Independent School</h2>
                    <a href="/school-directory/school/1002">Profile</a>
                    <a href="https://harborindependent.edu">Website</a>
                    <a href="mailto:hello@harborindependent.edu">hello@harborindependent.edu</a>
                    <span>+1-555-0200</span>
                  </div>
                  <div class="listing-card">
                    <h2>Get Direction</h2>
                    <a href="https://www.google.com/maps/dir">Maps</a>
                  </div>
                </body></html>
                """));
        fetcher.Map(
            "https://www.nais.org/school-directory/school/1001",
            FakeSafeHttpFetcher.Html(
                "https://www.nais.org/school-directory/school/1001",
                """
                <html><body>
                  <h1>Riverside Prep Academy</h1>
                  <p>Private independent school in California, United States.</p>
                  <a href="https://riversideprep.edu">Official website</a>
                  <a href="mailto:office@riversideprep.edu">office@riversideprep.edu</a>
                </body></html>
                """));
        fetcher.Map(
            "https://www.nais.org/school-directory/school/1002",
            FakeSafeHttpFetcher.Html(
                "https://www.nais.org/school-directory/school/1002",
                """
                <html><body>
                  <h1>Harbor Independent School</h1>
                  <p>Private school in Boston, United States.</p>
                  <a href="https://harborindependent.edu">Official website</a>
                  <a href="mailto:hello@harborindependent.edu">hello@harborindependent.edu</a>
                </body></html>
                """));
        fetcher.Map(
            "https://riversideprep.edu",
            FakeSafeHttpFetcher.Html(
                "https://riversideprep.edu",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"EducationalOrganization","name":"Riverside Prep Academy","url":"https://riversideprep.edu","telephone":"+1-555-0100","address":{"addressCountry":"US","addressRegion":"CA"}}
                  </script>
                </head>
                <body>
                  Riverside Prep Academy is a private independent school in California, United States.
                  <a href="https://riversideprep.edu/contact">Contact</a>
                </body></html>
                """));
        fetcher.Map(
            "https://riversideprep.edu/contact",
            FakeSafeHttpFetcher.Html(
                "https://riversideprep.edu/contact",
                """
                <html><body>
                  Email admissions at <a href="mailto:admissions@riversideprep.edu">admissions@riversideprep.edu</a>
                  or office at <a href="mailto:office@riversideprep.edu">office@riversideprep.edu</a>.
                  Phone <a href="tel:+15550100">+1-555-0100</a>
                </body></html>
                """));
        fetcher.Map(
            "https://harborindependent.edu",
            FakeSafeHttpFetcher.Html(
                "https://harborindependent.edu",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"EducationalOrganization","name":"Harbor Independent School","url":"https://harborindependent.edu","telephone":"+1-555-0200","address":{"addressCountry":"US","addressRegion":"MA"}}
                  </script>
                </head>
                <body>
                  Harbor Independent School is a private school in Boston, United States.
                  <a href="https://harborindependent.edu/contact">Contact</a>
                </body></html>
                """));
        fetcher.Map(
            "https://harborindependent.edu/contact",
            FakeSafeHttpFetcher.Html(
                "https://harborindependent.edu/contact",
                """
                <html><body>
                  Contact <a href="mailto:info@harborindependent.edu">info@harborindependent.edu</a>
                  and <a href="mailto:admissions@harborindependent.edu">admissions@harborindependent.edu</a>.
                  Call <a href="tel:+15550200">+1-555-0200</a>
                </body></html>
                """));
    }

    private static IUrlDiscoverySource[] CreateSeeds()
        =>
        [
            new FixedDiscoverySource(
                "DirectoryCatalog",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://www.nais.org/school-directory",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "NAIS Independent Schools",
                        Depth = 0,
                        PriorityBoost = 25,
                        IsIndustryRelevant = true
                    }
                ])
        ];

    private static IndependentCrawlProspectSource CreateCrawler(
        SendlyDbContext db,
        FakeSafeHttpFetcher fetcher,
        IEnumerable<IUrlDiscoverySource> sources)
    {
        var options = MsOptions.Create(new ResearchOptions
        {
            EnableIndependentCrawler = true,
            EnableCommonCrawlDiscovery = false,
            MaxFetchesPerRun = 30,
            MaxPagesPerCrawl = 8,
            MaxDiscoveryCandidates = 80,
            MaxPriorResearchSharePercent = 40,
            EvidenceReuseMaxAgeDays = 30,
            MaxHostFetchFailuresBeforeAvoid = 2,
            MaxResearchDurationMinutes = 30
        });
        var coordinator = new AutomaticDiscoveryCoordinator(sources, NullLogger<AutomaticDiscoveryCoordinator>.Instance);
        var notifier = new Mock<IResearchProgressNotifier>();
        notifier.Setup(x => x.NotifyEventAsync(It.IsAny<DTOs.ResearchRunEventDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        notifier.Setup(x => x.NotifyProgressAsync(It.IsAny<DTOs.ResearchRunDetailDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var events = new ResearchEventRecorder(db, notifier.Object);
        var registry = new DiscoverySourceRegistryService(db);
        var expansion = new DiscoverySourceExpansionService(
            registry,
            fetcher,
            MsOptions.Create(new ResearchOptions { EnableDiscoverySourceExpansion = false }));

        return new IndependentCrawlProspectSource(
            db,
            coordinator,
            fetcher,
            new FakeRobotsTxtService(),
            new SitemapParser(),
            expansion,
            registry,
            sources,
            events,
            options,
            NullLogger<IndependentCrawlProspectSource>.Instance);
    }

    private static TestableResearchProcessor CreateProcessor(
        SendlyDbContext db,
        IndependentCrawlProspectSource crawler)
    {
        var notifier = new Mock<IResearchProgressNotifier>();
        notifier.Setup(x => x.NotifyEventAsync(It.IsAny<DTOs.ResearchRunEventDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        notifier.Setup(x => x.NotifyProgressAsync(It.IsAny<DTOs.ResearchRunDetailDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var events = new ResearchEventRecorder(db, notifier.Object);
        return new TestableResearchProcessor(
            db,
            [crawler],
            new RuleBasedAiResearchService(),
            new ProspectDuplicateService(db),
            new ProspectQualificationService(),
            new CrawlRetentionService(db, NullLogger<CrawlRetentionService>.Instance),
            new DiscoverySourceRegistryService(db),
            new ResearchCancellationRegistry(),
            notifier.Object,
            events,
            MsOptions.Create(new ResearchOptions()),
            NullLogger<ResearchProcessor>.Instance);
    }

    private static ProspectSourceContext CreateContext(
        OutreachProject project,
        ResearchRun run,
        ResearchDiscoveryState state)
        => new()
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            ProjectName = project.Name,
            Countries = ["United States"],
            Industries = ["Private School", "Independent School"],
            Keywords = ["private school", "independent school"],
            DiscoveryBatchSize = 10,
            DiscoveryState = state
        };

    private static SendlyDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SendlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SendlyDbContext(options);
    }

    private static async Task<OutreachProject> SeedProjectAsync(SendlyDbContext db)
    {
        var project = new OutreachProject
        {
            Name = "Deep Contact Acceptance",
            Description = "US private schools",
            ResearchInstructions = "Extract multiple contacts"
        };
        project.Countries.Add(new OutreachProjectCountry { Name = "United States" });
        project.Industries.Add(new OutreachProjectIndustry { Value = "Private School" });
        project.Keywords.Add(new OutreachProjectKeyword { Value = "private school" });
        db.OutreachProjects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private static async Task<ResearchRun> SeedRunAsync(SendlyDbContext db, OutreachProject project)
    {
        var run = new ResearchRun
        {
            ProjectId = project.Id,
            Status = ResearchRunStatus.Running,
            RequestedProspectCount = 10,
            IncludeEmail = true,
            MinQualificationScore = 60,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.ResearchRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private sealed class FixedDiscoverySource : IUrlDiscoverySource
    {
        private readonly List<DiscoveredUrlCandidate> _candidates;

        public FixedDiscoverySource(string name, List<DiscoveredUrlCandidate> candidates)
        {
            Name = name;
            _candidates = candidates;
        }

        public string Name { get; }

        public bool IsEnabled(UrlDiscoveryContext context) => true;

        public Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
            UrlDiscoveryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DiscoveredUrlCandidate>>(_candidates);
    }
}

public class TestableResearchProcessor : ResearchProcessor
{
    public TestableResearchProcessor(
        ISendlyDbContext db,
        IEnumerable<IProspectSource> sources,
        IAiResearchService ai,
        ProspectDuplicateService duplicates,
        ProspectQualificationService qualificationService,
        CrawlRetentionService retentionService,
        DiscoverySourceRegistryService discoverySourceRegistry,
        ResearchCancellationRegistry cancellationRegistry,
        IResearchProgressNotifier notifier,
        IResearchEventRecorder events,
        Microsoft.Extensions.Options.IOptions<ResearchOptions> options,
        Microsoft.Extensions.Logging.ILogger<ResearchProcessor> logger)
        : base(
            db,
            sources,
            ai,
            duplicates,
            qualificationService,
            retentionService,
            discoverySourceRegistry,
            cancellationRegistry,
            notifier,
            events,
            options,
            logger)
    {
    }

    public Task ProcessCandidateForTestsAsync(
        OutreachProject project,
        ResearchRun run,
        ProspectCandidate candidate,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
        => ProcessCandidateAsync(project, run, candidate, state, cancellationToken);
}
