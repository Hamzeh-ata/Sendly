using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Application.Tests.Fakes;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Infrastructure.Persistence;
using Sendly.Infrastructure.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Sendly.Application.Tests;

public class IndependentCrawlEndToEndTests
{
    [Fact]
    public async Task DiscoverAsync_end_to_end_filters_assets_prioritizes_contact_and_extracts_json_ld()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);

        var fetcher = new FakeSafeHttpFetcher();
        fetcher.Map(
            "https://example.com/directory",
            FakeSafeHttpFetcher.Html(
                "https://example.com/directory",
                """
                <html><head><title>Business Directory</title></head>
                <body>
                  <a href="https://example.com/acme-clean">Acme Clean</a>
                  <a href="https://example.com/assets/app.js">script</a>
                  <img src="https://images.squarespace-cdn.com/photo.jpg" />
                </body></html>
                """));
        fetcher.Map(
            "https://example.com/acme-clean",
            FakeSafeHttpFetcher.Html(
                "https://example.com/acme-clean",
                """
                <html><head>
                  <title>Acme Cleaning | Home</title>
                  <script type="application/ld+json">
                  {"@type":"LocalBusiness","name":"Acme Cleaning Co","url":"https://example.com/acme-clean","email":"hello@example.com","telephone":"+1-555-0100"}
                  </script>
                  <meta property="og:site_name" content="Acme Cleaning Co" />
                </head>
                <body>
                  <a href="https://example.com/acme-clean/contact">Contact</a>
                  <a href="https://example.com/acme-clean/about">About</a>
                  Acme Cleaning Co provides commercial cleaning in Saudi Arabia.
                </body></html>
                """));
        fetcher.Map(
            "https://example.com/acme-clean/contact",
            FakeSafeHttpFetcher.Html(
                "https://example.com/acme-clean/contact",
                """
                <html><head><title>Contact Acme Cleaning Co</title></head>
                <body>Email us at <a href="mailto:hello@example.com">hello@example.com</a></body></html>
                """));
        fetcher.Map(
            "https://example.com/blocked-directory",
            FakeSafeHttpFetcher.Forbidden("https://example.com/blocked-directory"));
        fetcher.Map(
            "https://example.com/robots-blocked",
            FakeSafeHttpFetcher.RobotsBlocked("https://example.com/robots-blocked"));

        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "DirectoryCatalog",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/directory",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "Test Directory",
                        Depth = 0
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/assets/app.js",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "Noise",
                        Depth = 0
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://images.squarespace-cdn.com/photo.jpg",
                        SourceType = DiscoverySourceType.LinkExpansion,
                        SourceName = "Noise",
                        Depth = 1
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/blocked-directory",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "Blocked",
                        Depth = 0
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/robots-blocked",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "Robots",
                        Depth = 0
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/acme-clean",
                        SourceType = DiscoverySourceType.SeedUrl,
                        SourceName = "Fresh official",
                        Depth = 0
                    }
                ]),
            new FixedDiscoverySource("PriorResearchUrls", [])
        };

        var crawler = CreateCrawler(db, fetcher, sources);
        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state);

        var batch1 = await crawler.DiscoverAsync(context, CancellationToken.None);
        Assert.NotEmpty(batch1);

        var discovered = await db.DiscoveredUrls.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        Assert.DoesNotContain(discovered, x => x.Url.Contains("squarespace", StringComparison.OrdinalIgnoreCase)
            && x.Status == DiscoveredUrlStatus.Pending);
        Assert.DoesNotContain(discovered, x => x.Url.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            && x.Status == DiscoveredUrlStatus.Pending);

        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("acme-clean", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("squarespace", StringComparison.OrdinalIgnoreCase));

        var acme = batch1.FirstOrDefault(x => x.CompanyName.Contains("Acme", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(acme);
        Assert.Equal("hello@example.com", acme!.Email);
        Assert.Equal(ContactAttributionState.SameDomain, acme.ContactAttributionState);
        Assert.Contains("json_ld", acme.NameReasonCode ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var forbidden = discovered.First(x => x.Url.Contains("blocked-directory", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(DiscoveredUrlStatus.Failed, forbidden.Status);
        Assert.Equal("fetch.forbidden", forbidden.RejectionReasonCode);

        var robots = discovered.First(x => x.Url.Contains("robots-blocked", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(DiscoveredUrlStatus.SkippedRobots, robots.Status);

        var events = await db.ResearchRunEvents.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        Assert.Contains(events, x => x.EventType == ResearchRunEventType.FetchFailed);
        Assert.Contains(events, x => x.EventType == ResearchRunEventType.RobotsDenied);
        Assert.DoesNotContain(
            events.Where(x => x.Url != null && x.Url.Contains("blocked-directory", StringComparison.OrdinalIgnoreCase)),
            x => x.EventType == ResearchRunEventType.RetryScheduled);

        Assert.True(state.UrlsRejectedFiltered >= 1 || discovered.Any(x => x.Status == DiscoveredUrlStatus.SkippedFiltered));
        Assert.True(state.PagesFetched > 0);
        Assert.True(fetcher.RequestedUrls.Count >= state.PagesFetched);
        Assert.Contains(state.FetchFailureReasons.Keys, k => k.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
            || k.Contains("robots", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverAsync_skips_fresh_contacted_prospect_without_refetch()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        db.Prospects.Add(new Prospect
        {
            ProjectId = project.Id,
            CompanyName = "Known Clean",
            Website = "https://example.com/known-clean",
            Email = "info@example.com",
            Status = ProspectStatus.Contacted,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var fetcher = new FakeSafeHttpFetcher();
        fetcher.Map(
            "https://example.com/known-clean",
            FakeSafeHttpFetcher.Html("https://example.com/known-clean", "<html><title>Known Clean</title></html>"));
        fetcher.Map(
            "https://example.com/new-clean",
            FakeSafeHttpFetcher.Html(
                "https://example.com/new-clean",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"Organization","name":"New Clean Co","email":"team@example.com"}
                  </script>
                </head>
                <body>New Clean Co commercial cleaning Saudi Arabia</body></html>
                """));

        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "PriorResearchUrls",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/known-clean",
                        SourceType = DiscoverySourceType.PriorResearch,
                        SourceName = "Prior",
                        Depth = 0
                    }
                ]),
            new FixedDiscoverySource(
                "SeedUrls",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/new-clean",
                        SourceType = DiscoverySourceType.SeedUrl,
                        SourceName = "Fresh official",
                        Depth = 0
                    }
                ])
        };

        var crawler = CreateCrawler(db, fetcher, sources);
        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state);
        var batch = await crawler.DiscoverAsync(context, CancellationToken.None);

        Assert.DoesNotContain(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("known-clean", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("new-clean", StringComparison.OrdinalIgnoreCase));
        Assert.True(state.UrlsSkippedKnownProspect >= 1);
        Assert.True(state.UrlsReusedFromCache >= 1);
        Assert.Contains(batch, x => x.CompanyName.Contains("New Clean", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverAsync_directory_expansion_recovers_official_domains()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);

        var fetcher = new FakeSafeHttpFetcher();
        fetcher.Map(
            "https://example.org/directory-list",
            FakeSafeHttpFetcher.Html(
                "https://example.org/directory-list",
                """
                <html><head><title>Business Directory</title></head>
                <body>
                  <a href="https://example.com/northridge-clean">North Ridge</a>
                  <a href="https://example.net/calder-clean/contact">Calder Contact</a>
                  <a href="https://example.org/careers">Jobs</a>
                </body></html>
                """));
        fetcher.Map(
            "https://example.com/northridge-clean",
            FakeSafeHttpFetcher.Html(
                "https://example.com/northridge-clean",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"Organization","name":"North Ridge Cleaning","email":"office@example.com"}
                  </script>
                </head>
                <body>North Ridge Cleaning Saudi Arabia commercial cleaning</body></html>
                """));
        fetcher.Map(
            "https://example.net/calder-clean/contact",
            FakeSafeHttpFetcher.Html(
                "https://example.net/calder-clean/contact",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"Organization","name":"Calder Cleaning","email":"hello@example.net"}
                  </script>
                </head>
                <body>Contact Calder Cleaning</body></html>
                """));

        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "DirectoryCatalog",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.org/directory-list",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "Catalog",
                        Depth = 0
                    }
                ])
        };

        var crawler = CreateCrawler(db, fetcher, sources);
        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state);

        await crawler.DiscoverAsync(context, CancellationToken.None);
        var discovered = await db.DiscoveredUrls.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        Assert.Contains(discovered, x => x.Url.Contains("example.org/directory-list", StringComparison.OrdinalIgnoreCase));

        var crawled = await db.CrawledPages.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        Assert.Contains(crawled, x => !string.IsNullOrWhiteSpace(x.ExternalLinksJson)
            && x.ExternalLinksJson!.Contains("northridge-clean", StringComparison.OrdinalIgnoreCase));

        var page = crawled.First(x => !string.IsNullOrWhiteSpace(x.ExternalLinksJson));
        var links = System.Text.Json.JsonSerializer.Deserialize<List<string>>(page.ExternalLinksJson!) ?? [];
        var recovery = OfficialDomainRecovery.BuildCandidates(
            links,
            parentUrl: page.Url,
            knownDomains: ["example.org"],
            knownCanonicalUrls: discovered.Select(x => x.NormalizedUrl ?? x.Url).ToList(),
            industries: context.Industries,
            countries: context.Countries,
            maxCandidates: 10);

        Assert.True(recovery.Candidates.Count >= 1);
        Assert.Contains(recovery.Candidates, x => x.Url.Contains("example.com", StringComparison.OrdinalIgnoreCase)
            || x.Url.Contains("example.net", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(recovery.Candidates, x => x.Url.Contains("/careers", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("recovery.official_domains_added", recovery.OutcomeReason);
    }

    [Fact]
    public void Qualification_missing_email_keeps_fit_but_not_email_ready()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Acme Cleaning Co",
                Website = "https://example.com/acme-clean",
                Country = "Saudi Arabia",
                CountryEvidenceSource = "page_address",
                CountryEvidenceSummary = "Located in Riyadh, Saudi Arabia",
                Industry = "Cleaning",
                CompanyDescription = "Commercial cleaning in Saudi Arabia",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 80,
                NameConfidence = 90,
                NameReasonCode = "identity.json_ld_organization"
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["Saudi Arabia"],
            Industries = ["Cleaning"]
        });

        Assert.False(result.IsQualified);
        Assert.False(result.IsEmailReady);
        Assert.False(result.IsContactable);
        Assert.True(result.IsIdentityVerified);
        Assert.Contains("publicBusinessEmail", result.MissingCriteria);
    }

    [Fact]
    public void Mandatory_entity_failure_cannot_be_rescued_by_high_score_inputs()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Best Companies Near Me Directory",
                Website = "https://directory.example",
                Country = "Saudi Arabia",
                Industry = "Cleaning",
                Email = "ads@directory.example",
                EntityType = ResearchEntityType.DirectoryAggregator,
                EntityTypeConfidence = 95,
                NameConfidence = 90
            },
            RequireEmail = true,
            MinQualificationScore = 50,
            Countries = ["Saudi Arabia"],
            Industries = ["Cleaning"],
            Keywords = ["cleaning", "directory", "best", "companies"]
        });

        Assert.False(result.IsQualified);
        Assert.True(result.Score < 50);
        Assert.Contains("entityType", result.MissingCriteria);
    }

    [Fact]
    public void HtmlExtractor_reads_json_ld_and_open_graph()
    {
        var html = """
            <html><head>
              <title>Generic Title</title>
              <meta property="og:site_name" content="Acme Cleaning Co" />
              <script type="application/ld+json">
              {"@type":"LocalBusiness","name":"Acme Cleaning Co","email":"hello@acme.example","telephone":"+966500000000"}
              </script>
            </head><body>Hello</body></html>
            """;
        var extracted = HtmlPageExtractor.Extract(html, new Uri("https://acme.example/"));
        Assert.Equal("Acme Cleaning Co", extracted.OrganizationName);
        Assert.Equal("hello@acme.example", extracted.Emails[0]);
        Assert.True(extracted.UsedStructuredData);
        Assert.Contains(extracted.Facts, x => x.FactType == "organizationName" && x.ExtractionMethod == "json_ld.organization");
    }

    [Fact]
    public void FetchFailureClassifier_marks_403_non_retryable()
    {
        var result = FetchFailureClassifier.Classify(FakeSafeHttpFetcher.Forbidden("https://example.com"));
        Assert.Equal(FetchFailureKind.Forbidden, result.Kind);
        Assert.False(result.RetryAppropriate);
        Assert.True(result.HostShouldBeAvoided);
    }

    private static IndependentCrawlProspectSource CreateCrawler(
        SendlyDbContext db,
        FakeSafeHttpFetcher fetcher,
        IEnumerable<IUrlDiscoverySource> sources)
    {
        var options = MsOptions.Create(new ResearchOptions
        {
            EnableIndependentCrawler = true,
            EnableCommonCrawlDiscovery = false,
            MaxFetchesPerRun = 20,
            MaxPagesPerCrawl = 5,
            MaxDiscoveryCandidates = 50,
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

    private static ProspectSourceContext CreateSourceContext(
        OutreachProject project,
        ResearchRun run,
        ResearchDiscoveryState state)
        => new()
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            ProjectName = project.Name,
            Countries = ["Saudi Arabia"],
            Industries = ["Cleaning"],
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
            Name = "E2E Project",
            Description = "Cleaning outreach",
            ResearchInstructions = "Find cleaning companies"
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
