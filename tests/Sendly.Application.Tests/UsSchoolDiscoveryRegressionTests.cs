using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Application.Services.WebsiteInvestigation;
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

public class UsSchoolDiscoveryRegressionTests
{
    [Fact]
    public async Task A_UsSchoolDiscovery_ExtractsMultipleSchoolsAndFollowsOfficialSites()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        MapUsDirectoryFixture(fetcher);

        var crawler = CreateCrawler(db, fetcher, CreateUsDirectorySeeds());
        var state = new ResearchDiscoveryState();
        var context = CreateUsContext(project, run, state);

        var batch = new List<ProspectCandidate>();
        for (var i = 0; i < 4; i++)
        {
            var round = await crawler.DiscoverAsync(context, CancellationToken.None);
            batch.AddRange(round);
            if (fetcher.RequestedUrls.Any(u => u.AbsoluteUri.Contains("/contact", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }
        }

        Assert.Contains(batch, x => x.CompanyName.Contains("Riverside Prep", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(batch, x => x.CompanyName.Contains("Harbor Independent", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("riversideprep.edu", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("harborindependent.edu", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("/contact", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void B_InternationalDirectory_RejectsForeignPathsForUsQuery()
    {
        var foreign = UrlAdmissionPolicy.Evaluate(
            "https://www.theinternationalschools.com/vn/school/unis-hanoi/",
            countries: ["United States"]);
        var usListing = UrlAdmissionPolicy.Evaluate(
            "https://www.privateschoolreview.com/california",
            countries: ["United States"]);
        var swiss = UrlAdmissionPolicy.Evaluate(
            "https://www.theinternationalschools.com/ch/school/prefleuri/",
            countries: ["United States"]);

        Assert.False(foreign.Accepted);
        Assert.Equal("url.foreign_path", foreign.ReasonCode);
        Assert.False(swiss.Accepted);
        Assert.True(usListing.Accepted);
    }

    [Fact]
    public void C_MandatoryCountryGate_RejectsForeignSchoolWithEmailAndHighScoreSignals()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            Candidate = new ProspectCandidate
            {
                CompanyName = "United Nations International School of Hanoi",
                Website = "https://unishanoi.org",
                Country = "United States",
                CountryEvidenceSource = "query_target_stamp",
                Industry = "Private School",
                CompanyDescription = "International school in Hanoi, Vietnam",
                Email = "info@unishanoi.org",
                SourceUrl = "https://www.theinternationalschools.com/vn/school/unis-hanoi/",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 90,
                NameConfidence = 90,
                NameReasonCode = "identity.title_corroborated",
                ContactAttributionState = ContactAttributionState.SameDomain,
                ContactAttributionConfidence = 90,
                ContactAttributionReasonCode = "contact.same_domain"
            },
            QueryVersion = new ResearchQueryVersion
            {
                RequireOfficialWebsite = true,
                RequirePublicBusinessEmail = true,
                TargetCountry = "United States",
                TargetIndustry = "Private School"
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["United States"],
            Industries = ["Private School"],
            Keywords = ["private school", "international school"]
        });

        Assert.False(result.IsQualified);
        Assert.Contains("targetCountry", result.MissingCriteria);
        Assert.True(result.Score < 60);
        var country = result.Criteria.First(x => x.CriterionKey == "targetCountry");
        Assert.False(country.Passed);
        Assert.True(country.IsMandatory);
        Assert.Contains("Vietnam", country.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task D_AssociationNavigation_DoesNotMonopolizeBudget()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();

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
                  </div>
                  <a href="https://www.nais.org/about/history-of-nais">History</a>
                  <a href="https://www.nais.org/about/nais-public-policy-positions">Policy</a>
                  <a href="https://sso.nais.org/sessions/new">Login</a>
                  <a href="https://www.closeup.org/events">Close Up</a>
                  <a href="https://www.nais.org/careers">Careers</a>
                </body></html>
                """));
        fetcher.Map(
            "https://www.nais.org/school-directory/school/1001",
            FakeSafeHttpFetcher.Html(
                "https://www.nais.org/school-directory/school/1001",
                """
                <html><body>
                  <h1>Riverside Prep Academy</h1>
                  <p>United States private independent school in California.</p>
                  <a href="https://riversideprep.edu">Official website</a>
                </body></html>
                """));
        fetcher.Map(
            "https://riversideprep.edu",
            FakeSafeHttpFetcher.Html(
                "https://riversideprep.edu",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"EducationalOrganization","name":"Riverside Prep Academy","url":"https://riversideprep.edu"}
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
                <html><body>Email <a href="mailto:admissions@riversideprep.edu">admissions@riversideprep.edu</a></body></html>
                """));

        foreach (var noise in new[]
                 {
                     "https://www.nais.org/about/history-of-nais",
                     "https://www.nais.org/about/nais-public-policy-positions",
                     "https://sso.nais.org/sessions/new",
                     "https://www.closeup.org/events",
                     "https://www.nais.org/careers"
                 })
        {
            fetcher.Map(noise, FakeSafeHttpFetcher.Html(noise, "<html><body>noise</body></html>"));
        }

        var crawler = CreateCrawler(db, fetcher, [
            new FixedDiscoverySource(
                "DirectoryCatalog",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://www.nais.org/school-directory",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "NAIS Independent Schools",
                        Depth = 0,
                        PriorityBoost = 20
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://www.nais.org/about/history-of-nais",
                        SourceType = DiscoverySourceType.LinkExpansion,
                        SourceName = "noise",
                        Depth = 1
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://sso.nais.org/sessions/new",
                        SourceType = DiscoverySourceType.LinkExpansion,
                        SourceName = "noise",
                        Depth = 1
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://www.closeup.org/events",
                        SourceType = DiscoverySourceType.LinkExpansion,
                        SourceName = "noise",
                        Depth = 1
                    }
                ])
        ]);
        var state = new ResearchDiscoveryState();
        var context = CreateUsContext(project, run, state);
        for (var i = 0; i < 4; i++)
        {
            await crawler.DiscoverAsync(context, CancellationToken.None);
            if (fetcher.RequestedUrls.Any(u => u.AbsoluteUri.Contains("riversideprep.edu", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }
        }

        Assert.DoesNotContain(fetcher.RequestedUrls, u => u.Host.Contains("sso.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fetcher.RequestedUrls, u => u.Host.Contains("closeup.org", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("school-directory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("riversideprep.edu", StringComparison.OrdinalIgnoreCase));
        var schoolFetches = fetcher.RequestedUrls.Count(u =>
            u.AbsoluteUri.Contains("school-directory", StringComparison.OrdinalIgnoreCase)
            || u.AbsoluteUri.Contains("riversideprep.edu", StringComparison.OrdinalIgnoreCase));
        var chromeFetches = fetcher.RequestedUrls.Count(u =>
            u.AbsoluteUri.Contains("history-of-nais", StringComparison.OrdinalIgnoreCase)
            || u.AbsoluteUri.Contains("public-policy", StringComparison.OrdinalIgnoreCase)
            || u.AbsoluteUri.Contains("/careers", StringComparison.OrdinalIgnoreCase));
        Assert.True(schoolFetches > chromeFetches);
    }

    [Fact]
    public void E_ListingExtraction_KeepsSchoolsRejectsChrome()
    {
        var html = """
            <html><body>
              <div class="listing-card">
                <h2>Get Direction</h2>
                <a href="https://www.google.com/maps/dir">Maps</a>
              </div>
              <div class="listing-card">
                <h2>Join NAIS</h2>
                <a href="/membership">Membership</a>
              </div>
              <div class="listing-card">
                <h2>ACADEMIC CALENDAR</h2>
                <a href="/calendar">Calendar</a>
              </div>
              <div class="listing-card">
                <h2>Riverside Prep Academy</h2>
                <a href="/school-directory/school/1001">Profile</a>
                <a href="https://riversideprep.edu">Website</a>
              </div>
              <div class="listing-card">
                <h2>Harbor Independent School</h2>
                <a href="/school-directory/school/1002">Profile</a>
                <a href="https://harborindependent.edu">Website</a>
              </div>
            </body></html>
            """;

        var listings = DirectoryListingExtractor.Extract(html, new Uri("https://www.nais.org/school-directory"));
        Assert.DoesNotContain(listings, x => x.Name.Contains("Get Direction", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(listings, x => x.Name.Contains("Join NAIS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(listings, x => x.Name.Contains("ACADEMIC CALENDAR", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(listings, x => x.Name.Contains("Riverside Prep", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(listings, x => x.Name.Contains("Harbor Independent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task F_OfficialContactChain_AttributesEmailToSchool()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        MapUsDirectoryFixture(fetcher);

        var crawler = CreateCrawler(db, fetcher, CreateUsDirectorySeeds());
        var state = new ResearchDiscoveryState();
        var context = CreateUsContext(project, run, state);
        ProspectCandidate? riverside = null;
        for (var i = 0; i < 5; i++)
        {
            var batch = await crawler.DiscoverAsync(context, CancellationToken.None);
            riverside = batch.FirstOrDefault(x =>
                x.CompanyName.Contains("Riverside Prep", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.Email))
                ?? riverside
                ?? batch.FirstOrDefault(x =>
                    x.CompanyName.Contains("Riverside Prep", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(riverside?.Email))
            {
                break;
            }
        }

        Assert.NotNull(riverside);
        Assert.Equal("admissions@riversideprep.edu", riverside!.Email);
        Assert.Equal(ContactAttributionState.SameDomain, riverside.ContactAttributionState);
        Assert.Contains("riversideprep.edu", riverside.Website, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G_QueuePrioritization_PrefersListingsOverAssociationChrome()
    {
        var listing = UrlPriorityScorer.Score(
            "https://www.nais.org/school-directory/school/1001",
            DiscoverySourceType.DirectoryCatalog,
            ["Private School"],
            ["United States"],
            parentDomain: "nais.org",
            depth: 1,
            alreadyCrawled: false,
            pagePurpose: UrlPagePurpose.DirectoryListing);

        var chrome = UrlPriorityScorer.Score(
            "https://www.nais.org/about/nais-public-policy-positions",
            DiscoverySourceType.LinkExpansion,
            ["Private School"],
            ["United States"],
            parentDomain: "nais.org",
            depth: 1,
            alreadyCrawled: false,
            pagePurpose: UrlPagePurpose.DirectoryListing);

        var foreign = UrlPriorityScorer.Score(
            "https://www.theinternationalschools.com/vn/school/unis-hanoi/",
            DiscoverySourceType.LinkExpansion,
            ["Private School"],
            ["United States"],
            parentDomain: "theinternationalschools.com",
            depth: 1,
            alreadyCrawled: false,
            pagePurpose: UrlPagePurpose.CompanyProfile);

        Assert.True(listing.Total > chrome.Total);
        Assert.True(listing.Total > foreign.Total);
    }

    [Fact]
    public void H_NoUsefulUsSource_ReportsCatalogLimitation()
    {
        var resolved = SystemDirectoryCatalog.ResolveWithDecisions(
            ["United States"],
            ["Private School", "Independent School"]);

        Assert.DoesNotContain(resolved.Entries, x =>
            x.BaseUrl.Contains("opencorporates.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resolved.Eligibility.Decisions, x =>
            x.ReasonCode == "source.registry_low_value_for_schools"
            || x.SourceUrl?.Contains("opencorporates.com", StringComparison.OrdinalIgnoreCase) == true
               && x.State == SourceEligibilityState.Ineligible);
        Assert.Contains(resolved.Entries, x =>
            x.BaseUrl.Contains("nais.org/school-directory", StringComparison.OrdinalIgnoreCase)
            || x.PreferredListingUrls.Any(u => u.Contains("school-directory", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(resolved.Entries, x =>
            x.BaseUrl.Contains("privateschoolreview.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void I_Safety_SsrfAndAuthHostsStillRejected()
    {
        var loopback = UrlAdmissionPolicy.Evaluate("http://127.0.0.1/admin");
        var sso = UrlAdmissionPolicy.Evaluate("https://sso.nais.org/sessions/new");
        var metadata = UrlAdmissionPolicy.Evaluate("http://169.254.169.254/latest/meta-data");

        Assert.False(loopback.Accepted);
        Assert.False(sso.Accepted);
        Assert.Equal("url.auth_host", sso.ReasonCode);
        Assert.False(metadata.Accepted);
    }

    [Fact]
    public void J_OtherIndustries_CleaningAssociationsStillEligible()
    {
        var resolved = SystemDirectoryCatalog.ResolveWithDecisions(
            ["United States"],
            ["Commercial Cleaning"]);

        Assert.Contains(resolved.Entries, x => x.BaseUrl.Contains("issa.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resolved.Entries, x => x.BaseUrl.Contains("bscai.org", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resolved.Entries, x =>
            x.BaseUrl.Contains("nais.org", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CountryPathInference_ReadsLeadingIsoSegments()
    {
        Assert.Equal("Vietnam", CountryScopeHelper.InferCountryFromPath("/vn/school/unis-hanoi/"));
        Assert.Equal("Singapore", CountryScopeHelper.InferCountryFromPath("/sg/school/sas/"));
        Assert.Equal("United States", CountryScopeHelper.InferCountryFromPath("/us/school/example/"));
        Assert.True(CountryScopeHelper.PathLooksOutOfCountryScope("/my/school/marlborough/", ["United States"]));
        Assert.False(CountryScopeHelper.PathLooksOutOfCountryScope("/us/school/example/", ["United States"]));
    }

    private static void MapUsDirectoryFixture(FakeSafeHttpFetcher fetcher)
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
                  </div>
                  <div class="listing-card">
                    <h2>Harbor Independent School</h2>
                    <a href="/school-directory/school/1002">Profile</a>
                    <a href="https://harborindependent.edu">Website</a>
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
                  <p>Independent private school, California, United States.</p>
                  <a href="https://riversideprep.edu">Website</a>
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
                  <a href="https://harborindependent.edu">Website</a>
                </body></html>
                """));
        fetcher.Map(
            "https://riversideprep.edu",
            FakeSafeHttpFetcher.Html(
                "https://riversideprep.edu",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"EducationalOrganization","name":"Riverside Prep Academy","url":"https://riversideprep.edu","address":{"addressCountry":"US","addressRegion":"CA"}}
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
                <html><body>Email admissions at <a href="mailto:admissions@riversideprep.edu">admissions@riversideprep.edu</a></body></html>
                """));
        fetcher.Map(
            "https://harborindependent.edu",
            FakeSafeHttpFetcher.Html(
                "https://harborindependent.edu",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"EducationalOrganization","name":"Harbor Independent School","url":"https://harborindependent.edu"}
                  </script>
                </head>
                <body>
                  Harbor Independent School is a private school in Boston, United States.
                  <a href="https://harborindependent.edu/admissions">Admissions</a>
                </body></html>
                """));
        fetcher.Map(
            "https://harborindependent.edu/admissions",
            FakeSafeHttpFetcher.Html(
                "https://harborindependent.edu/admissions",
                """
                <html><body>Contact <a href="mailto:info@harborindependent.edu">info@harborindependent.edu</a></body></html>
                """));
    }

    private static IUrlDiscoverySource[] CreateUsDirectorySeeds()
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
                        PriorityBoost = 20
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
            MaxFetchesPerRun = 25,
            MaxPagesPerCrawl = 6,
            MaxDiscoveryCandidates = 80,
            MaxPriorResearchSharePercent = 40,
            EvidenceReuseMaxAgeDays = 30,
            MaxHostFetchFailuresBeforeAvoid = 2,
            MaxResearchDurationMinutes = 30
        });
        var coordinator = new AutomaticDiscoveryCoordinator(sources, NullLogger<AutomaticDiscoveryCoordinator>.Instance);
        var notifier = new Mock<IResearchProgressNotifier>();
        notifier
            .Setup(x => x.NotifyEventAsync(It.IsAny<DTOs.ResearchRunEventDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        notifier
            .Setup(x => x.NotifyProgressAsync(It.IsAny<DTOs.ResearchRunDetailDto>(), It.IsAny<CancellationToken>()))
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

    private static ProspectSourceContext CreateUsContext(
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
            DiscoveryBatchSize = 12,
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
            Name = "School Watch - USA",
            Description = "Find private schools in the United States",
            ResearchInstructions = "US private schools only"
        };
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
