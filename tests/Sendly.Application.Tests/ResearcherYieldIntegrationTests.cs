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
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Sendly.Application.Tests;

public class ResearcherYieldIntegrationTests
{
    [Fact]
    public void Identity_json_ld_plus_contact_page_verifies_organization()
    {
        var identity = CompanyIdentityResolver.Resolve(
            "Contact us",
            "Maarif International School welcomes visitors. Email info@maarif.sa",
            "maarif.sa",
            "https://maarif.sa/contact",
            new CompanyIdentityEvidence
            {
                StructuredOrganizationName = "Maarif International School",
                PagePurpose = UrlPagePurpose.Contact,
                HasSameDomainEmail = true
            });

        Assert.True(identity.IsAcceptable);
        Assert.Equal("Maarif International School", identity.CompanyName);
        Assert.Equal("identity.json_ld_organization", identity.ReasonCode);
        Assert.True(identity.NameConfidence >= 55);
    }

    [Fact]
    public void Identity_generic_contact_title_is_not_organization_name()
    {
        var identity = CompanyIdentityResolver.Resolve(
            "Maarif: Contact us",
            "Please reach our admissions office.",
            "maarif.sa",
            "https://maarif.sa/contact");
        Assert.False(string.Equals(identity.CompanyName, "Contact us", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(identity.CompanyName, "Maarif: Contact us", StringComparison.OrdinalIgnoreCase));
        Assert.True(CompanyIdentityResolver.IsGenericName("Contact us"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Online Admission Form"));
    }

    [Fact]
    public void Identity_form_title_is_rejected()
    {
        var identity = CompanyIdentityResolver.Resolve(
            "Online Admission Form",
            "Fill the online admission form.",
            "school.sa",
            "https://school.sa/admission");
        Assert.False(identity.IsAcceptable);
        Assert.True(CompanyIdentityResolver.IsGenericName("Online Admission Form"));
    }

    [Fact]
    public void Identity_prior_first_party_name_corroborates_contact_page()
    {
        var identity = CompanyIdentityResolver.Resolve(
            "Contact us",
            "Write to our office at info@bright.sa for school tours.",
            "bright.sa",
            "https://bright.sa/contact",
            new CompanyIdentityEvidence
            {
                PriorOrganizationName = "Bright Academy",
                PagePurpose = UrlPagePurpose.Contact,
                HasSameDomainEmail = true,
                CorroboratingFirstPartyPages = 1
            });

        Assert.True(identity.IsAcceptable);
        Assert.Equal("Bright Academy", identity.CompanyName);
        Assert.Equal("identity.prior_first_party", identity.ReasonCode);
    }

    [Fact]
    public void Identity_email_alone_does_not_verify_organization()
    {
        var identity = CompanyIdentityResolver.Resolve(
            "Home",
            "Email us anytime.",
            "mystery.com",
            "https://mystery.com/",
            new CompanyIdentityEvidence
            {
                HasSameDomainEmail = true,
                PagePurpose = UrlPagePurpose.CompanyProfile
            });
        Assert.False(identity.IsAcceptable);
    }

    [Fact]
    public void Identity_verified_without_email_remains_identity_verified()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Bright Academy",
                Website = "https://bright.sa",
                Country = "Saudi Arabia",
                Industry = "Education",
                CompanyDescription = "Bright Academy private school in Riyadh.",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 70,
                NameConfidence = 92,
                NameReasonCode = "identity.json_ld_organization"
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["Saudi Arabia"],
            Industries = ["Education", "Schools"]
        });

        Assert.True(result.IsIdentityVerified);
        Assert.False(result.IsEmailReady);
        Assert.DoesNotContain("companyIdentity", result.MissingCriteria);
    }

    [Fact]
    public void Industry_synonyms_match_without_exact_query_wording()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Najran Private School",
                Website = "https://najran-school.sa",
                Country = "Saudi Arabia",
                CompanyDescription = "A private academy serving families in Najran.",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 70,
                NameConfidence = 92,
                NameReasonCode = "identity.json_ld_organization"
            },
            RequireEmail = false,
            MinQualificationScore = 60,
            Countries = ["Saudi Arabia"],
            Industries = ["Education"]
        });

        Assert.True(result.Criteria.First(x => x.CriterionKey == "targetIndustry").Passed);
        Assert.True(result.IsIdentityVerified);
    }

    [Fact]
    public void Strong_score_does_not_override_mandatory_criteria()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Australian Schools Directory",
                Website = "https://www.australianschoolsdirectory.com.au",
                Country = "Australia",
                Industry = "Independent School",
                Email = "info@australianschoolsdirectory.com.au",
                EntityType = ResearchEntityType.DirectoryAggregator,
                EntityTypeConfidence = 90,
                NameConfidence = 90,
                NameReasonCode = "identity.title_corroborated",
                ContactAttributionState = ContactAttributionState.SameDomain,
                ContactAttributionConfidence = 90
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["Australia"],
            Industries = ["Independent School"],
            Keywords = ["school", "private school"]
        });

        Assert.False(result.IsQualified);
        Assert.Contains("entityType", result.MissingCriteria);
        Assert.True(result.Score < 70);
    }

    [Fact]
    public void Contact_same_domain_email_is_attributed_and_directory_email_is_not()
    {
        var ok = ContactAttributionHelper.AttributeBest(
            ["info@school.sa", "ads@directory.com"],
            "https://school.sa/contact",
            "https://school.sa",
            pageIsDirectory: false);
        Assert.Equal(ContactAttributionState.SameDomain, ok.State);
        Assert.Equal("info@school.sa", ok.Email);

        var directoryOfficial = ContactAttributionHelper.Attribute(
            "info@school.sa",
            "https://en.dlilsa.com/listing/1",
            "https://school.sa",
            pageIsDirectory: true);
        Assert.Equal(ContactAttributionState.SameDomain, directoryOfficial.State);
        Assert.Equal("contact.directory_official_domain", directoryOfficial.ReasonCode);

        var directoryOperator = ContactAttributionHelper.Attribute(
            "ads@directory.com",
            "https://en.dlilsa.com/listing/1",
            "https://school.sa",
            pageIsDirectory: true);
        Assert.Equal(ContactAttributionState.DirectoryListing, directoryOperator.State);
    }

    [Fact]
    public void UrlAdmission_rejects_technical_and_foreign_paths()
    {
        Assert.False(UrlAdmissionPolicy.Evaluate("https://example.com/xmlrpc.php").Accepted);
        Assert.False(UrlAdmissionPolicy.Evaluate("https://linktr.ee/someone").Accepted);
        Assert.False(UrlAdmissionPolicy.Evaluate("https://demo.hostingersite.com/page").Accepted);
        Assert.False(UrlAdmissionPolicy.Evaluate("http://127.0.1.1/ar/contact").Accepted);

        var foreign = UrlAdmissionPolicy.Evaluate(
            "https://sites.moe.gov.sa/cm/lb/contact",
            countries: ["Saudi Arabia"]);
        Assert.False(foreign.Accepted);
        Assert.Equal("url.foreign_path", foreign.ReasonCode);

        var local = UrlAdmissionPolicy.Evaluate(
            "https://example.com/school/contact",
            countries: ["Saudi Arabia"]);
        Assert.True(local.Accepted);
    }

    [Fact]
    public void Scheduler_applies_adaptive_host_allowance_for_strong_org_hosts()
    {
        var baseline = CrawlQueueScheduler.ResolveHostFetchLimit(5, false, true);
        var adaptive = CrawlQueueScheduler.ResolveHostFetchLimit(5, true, true);
        Assert.Equal(5, baseline);
        Assert.Equal(9, adaptive);

        var pending = new List<DiscoveredUrl>();
        for (var i = 0; i < 8; i++)
        {
            pending.Add(new DiscoveredUrl
            {
                Url = $"https://school.sa/page-{i}/contact",
                NormalizedUrl = $"https://school.sa/page-{i}/contact",
                NormalizedDomain = "school.sa",
                SourceType = DiscoverySourceType.Sitemap,
                Status = DiscoveredUrlStatus.Pending,
                Depth = 1,
                PagePurpose = UrlPagePurpose.Contact.ToString(),
                PriorityScore = 90 - i,
                DiscoveredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-i)
            });
        }

        pending.Add(new DiscoveredUrl
        {
            Url = "https://portal.gov.sa/cm/jp/contact",
            NormalizedUrl = "https://portal.gov.sa/cm/jp/contact",
            NormalizedDomain = "portal.gov.sa",
            SourceType = DiscoverySourceType.Sitemap,
            Status = DiscoveredUrlStatus.Pending,
            Depth = 1,
            PagePurpose = UrlPagePurpose.Contact.ToString(),
            PriorityScore = 95,
            DiscoveredAtUtc = DateTimeOffset.UtcNow
        });

        var fetched = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["school.sa"] = 5
        };
        var strong = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "school.sa" };
        var limits = CrawlQueueScheduler.BuildHostFetchLimits(pending, 5, strong);
        var ranked = CrawlQueueScheduler.RankPending(
            pending,
            ["Education"],
            ["Saudi Arabia"],
            fetched,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            maxPagesPerDomain: 5,
            maxPerDomainInBatch: 2,
            limits,
            strong);

        Assert.Contains(ranked, x => x.Domain.Equals("school.sa", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ranked, x => x.Url.Url.Contains("/cm/jp/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Directory_host_does_not_supply_official_identity()
    {
        Assert.True(SystemDirectoryCatalog.IsSeedOrDirectoryHost("en.dlilsa.com"));
        var identity = CompanyIdentityResolver.Resolve(
            "Najran School",
            "School listing",
            "en.dlilsa.com",
            "https://en.dlilsa.com/school/1",
            "Najran School");
        Assert.False(identity.IsAcceptable);
        Assert.Equal("identity.directory_host", identity.ReasonCode);
    }

    [Fact]
    public async Task DiscoverAsync_contact_page_updates_existing_organization_with_email()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();

        fetcher.Map(
            "https://example.com/bright/",
            FakeSafeHttpFetcher.Html(
                "https://example.com/bright/",
                """
                <html><head>
                  <title>Bright Academy | Home</title>
                  <script type="application/ld+json">
                  {"@type":"EducationalOrganization","name":"Bright Academy","url":"https://example.com/bright/"}
                  </script>
                  <meta property="og:site_name" content="Bright Academy" />
                </head>
                <body>
                  Bright Academy is a private school in Saudi Arabia.
                  <a href="https://example.com/bright/contact">Contact</a>
                </body></html>
                """));
        fetcher.Map(
            "https://example.com/bright/contact",
            FakeSafeHttpFetcher.Html(
                "https://example.com/bright/contact",
                """
                <html><head><title>Contact us</title></head>
                <body>
                  Contact Bright Academy admissions.
                  Email <a href="mailto:info@example.com">info@example.com</a>
                </body></html>
                """));

        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "SeedUrls",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/bright/",
                        SourceType = DiscoverySourceType.SeedUrl,
                        SourceName = "Seed",
                        Depth = 0
                    }
                ])
        };

        var crawler = CreateCrawler(db, fetcher, sources, maxFetchesPerRun: 10, maxPagesPerDomain: 5);
        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state);

        var batch1 = await crawler.DiscoverAsync(context, CancellationToken.None);
        var home = batch1.FirstOrDefault(x => x.CompanyName.Contains("Bright", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(home);
        Assert.True(home!.NameConfidence >= 55);
        Assert.Equal("Bright Academy", home.CompanyName);

        var batch2 = await crawler.DiscoverAsync(context, CancellationToken.None);
        var all = batch1.Concat(batch2).ToList();
        var contact = all.FirstOrDefault(x =>
            x.CompanyName.Contains("Bright", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(x.Email));
        Assert.NotNull(contact);
        Assert.Equal("info@example.com", contact!.Email);
        Assert.Equal(ContactAttributionState.SameDomain, contact.ContactAttributionState);
        Assert.Contains("Bright", contact.CompanyName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(all, x =>
            string.Equals(x.CompanyName, "Contact us", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverAsync_drains_eligible_work_before_recovery_and_uses_adaptive_host_budget()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        var seeds = new List<DiscoveredUrlCandidate>();

        var hosts = new[] { "example.com", "example.org", "example.net" };
        for (var i = 0; i < 6; i++)
        {
            var host = hosts[i % hosts.Length];
            var home = $"https://{host}/school-{i}/";
            var about = $"https://{host}/school-{i}/about";
            var contact = $"https://{host}/school-{i}/contact";
            seeds.Add(new DiscoveredUrlCandidate
            {
                Url = home,
                SourceType = DiscoverySourceType.SeedUrl,
                SourceName = "Seed",
                Depth = 0
            });
            seeds.Add(new DiscoveredUrlCandidate
            {
                Url = about,
                SourceType = DiscoverySourceType.Sitemap,
                SourceName = "Sitemap",
                Depth = 1
            });
            seeds.Add(new DiscoveredUrlCandidate
            {
                Url = contact,
                SourceType = DiscoverySourceType.Sitemap,
                SourceName = "Sitemap",
                Depth = 1
            });
            seeds.Add(new DiscoveredUrlCandidate
            {
                Url = $"https://{host}/cm/jp/school-{i}/contact",
                SourceType = DiscoverySourceType.Sitemap,
                SourceName = "Portal",
                Depth = 1
            });

            MapSchoolPages(fetcher, home, about, contact, $"School {i} Academy", host);
        }

        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource("SeedUrls", seeds)
        };

        var crawler = CreateCrawler(db, fetcher, sources, maxFetchesPerRun: 30, maxPagesPerDomain: 3);
        var state = new ResearchDiscoveryState { ConfiguredBatchSize = 10, RequestedFetchBudget = 30 };
        var context = CreateSourceContext(project, run, state, discoveryBatchSize: 10);

        var all = new List<ProspectCandidate>();
        for (var batch = 0; batch < 5; batch++)
        {
            var pagesBefore = state.PagesFetched;
            var result = await crawler.DiscoverAsync(context, CancellationToken.None);
            all.AddRange(result);
            if (state.CompletedSourceNames.Contains("IndependentCrawler")
                || state.PagesFetched == pagesBefore)
            {
                break;
            }
        }

        Assert.True(state.PagesFetched >= 8, $"Expected multiple fetches, got {state.PagesFetched}");
        Assert.Equal(state.PagesFetched, state.FetchBudgetConsumed);
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("/contact", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            fetcher.RequestedUrls,
            u => u.AbsoluteUri.Contains("/cm/jp/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, x => x.NameConfidence >= 55);
        Assert.Contains(all, x => !string.IsNullOrWhiteSpace(x.Email));
        Assert.True(state.StrongOrganizationHosts.Count > 0);
        Assert.True(
            state.LastBudgetStopReason is "fetch.run_budget_exhausted"
                or "fetch.batch_limit"
                or "fetch.queue_empty"
                or "fetch.no_eligible_pending"
                or null);
    }

    [Fact]
    public async Task DiscoverAsync_keeps_official_school_prospect_from_seed()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();

        fetcher.Map(
            "https://example.com/official-school/",
            FakeSafeHttpFetcher.Html(
                "https://example.com/official-school/",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"School","name":"Official School","email":"hello@example.com"}
                  </script>
                </head>
                <body>Official School private education in Saudi Arabia.</body></html>
                """));

        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "SeedUrls",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/official-school/",
                        SourceType = DiscoverySourceType.SeedUrl,
                        SourceName = "Official",
                        Depth = 0
                    }
                ])
        };

        var crawler = CreateCrawler(db, fetcher, sources, maxFetchesPerRun: 10, maxPagesPerDomain: 5);
        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state);
        var candidates = await crawler.DiscoverAsync(context, CancellationToken.None);

        Assert.Contains(
            candidates,
            x => x.CompanyName.Contains("Official School", StringComparison.OrdinalIgnoreCase)
                 && x.Email == "hello@example.com"
                 && x.NameConfidence >= 55);
        Assert.True(SystemDirectoryCatalog.IsSeedOrDirectoryHost("en.dlilsa.com"));
    }

    [Fact]
    public async Task DiscoverAsync_does_not_retry_robots_or_permanent_failures()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        fetcher.Map("https://example.com/blocked/", FakeSafeHttpFetcher.RobotsBlocked("https://example.com/blocked/"));
        fetcher.Map("https://example.com/missing/", FakeSafeHttpFetcher.NotFound("https://example.com/missing/"));
        fetcher.Map(
            "https://example.org/good/",
            FakeSafeHttpFetcher.Html(
                "https://example.org/good/",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"Organization","name":"Good School","email":"info@example.org"}
                  </script>
                </head><body>Good School education Saudi Arabia</body></html>
                """));

        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "SeedUrls",
                [
                    new DiscoveredUrlCandidate { Url = "https://example.com/blocked/", SourceType = DiscoverySourceType.SeedUrl, SourceName = "Blocked", Depth = 0 },
                    new DiscoveredUrlCandidate { Url = "https://example.com/missing/", SourceType = DiscoverySourceType.SeedUrl, SourceName = "Missing", Depth = 0 },
                    new DiscoveredUrlCandidate { Url = "https://example.org/good/", SourceType = DiscoverySourceType.SeedUrl, SourceName = "Good", Depth = 0 }
                ])
        };

        var crawler = CreateCrawler(db, fetcher, sources, maxFetchesPerRun: 10, maxPagesPerDomain: 5);
        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state);
        await crawler.DiscoverAsync(context, CancellationToken.None);
        await crawler.DiscoverAsync(context, CancellationToken.None);

        Assert.Equal(1, fetcher.RequestedUrls.Count(u => u.AbsoluteUri.Contains("/blocked", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, fetcher.RequestedUrls.Count(u => u.AbsoluteUri.Contains("/missing", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("/good", StringComparison.OrdinalIgnoreCase));
    }

    private static void MapSchoolPages(
        FakeSafeHttpFetcher fetcher,
        string home,
        string about,
        string contact,
        string name,
        string host)
    {
        fetcher.Map(
            home,
            FakeSafeHttpFetcher.Html(
                home,
                "<html><head>"
                + $"<title>{name} | Home</title>"
                + "<script type=\"application/ld+json\">"
                + $"{{\"@type\":\"School\",\"name\":\"{name}\",\"url\":\"{home}\"}}"
                + "</script></head>"
                + $"<body>{name} private school education in Saudi Arabia."
                + $"<a href=\"{about}\">About</a><a href=\"{contact}\">Contact</a></body></html>"));
        fetcher.Map(
            about,
            FakeSafeHttpFetcher.Html(
                about,
                $"<html><head><title>About us</title></head><body>About {name} school community.</body></html>"));
        fetcher.Map(
            contact,
            FakeSafeHttpFetcher.Html(
                contact,
                "<html><head><title>Contact us</title></head><body>"
                + $"Contact {name}. Email <a href=\"mailto:info@{host}\">info@{host}</a>"
                + "</body></html>"));
    }

    private static IndependentCrawlProspectSource CreateCrawler(
        SendlyDbContext db,
        FakeSafeHttpFetcher fetcher,
        IEnumerable<IUrlDiscoverySource> sources,
        int maxFetchesPerRun,
        int maxPagesPerDomain)
    {
        var options = MsOptions.Create(new ResearchOptions
        {
            EnableIndependentCrawler = true,
            EnableCommonCrawlDiscovery = false,
            MaxFetchesPerRun = maxFetchesPerRun,
            MaxPagesPerCrawl = maxPagesPerDomain,
            MaxDiscoveryCandidates = 400,
            MaxCandidateDomains = 200,
            MaxPriorResearchSharePercent = 40,
            EvidenceReuseMaxAgeDays = 30,
            MaxHostFetchFailuresBeforeAvoid = 2,
            MaxResearchDurationMinutes = 30,
            DiscoveryOversampleFactor = 4
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
        ResearchDiscoveryState state,
        int discoveryBatchSize = 10)
        => new()
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            ProjectName = project.Name,
            Countries = ["Saudi Arabia"],
            Industries = ["Education", "Schools"],
            DiscoveryBatchSize = discoveryBatchSize,
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
            Name = "Yield Fixture",
            Description = "Education outreach",
            ResearchInstructions = "Find schools"
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
            RequestedProspectCount = 50,
            IncludeEmail = true,
            MinQualificationScore = 70,
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
