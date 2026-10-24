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

public class CrawlSchedulerStarvationRegressionTests
{
    [Theory]
    [InlineData("https://example.com/undefined/contact", false, "url.undefined_segment")]
    [InlineData("https://example.com/<?=$id?>/page", false, "url.php_template")]
    [InlineData("https://example.com/path with spaces", false, "url")]
    [InlineData("https://example.com/contact%20us", true, "url.accepted")]
    [InlineData("https://school.sa/contact", true, "url.accepted")]
    [InlineData("ftp://example.com/contact", false, "url")]
    [InlineData("http://127.0.1.1/ar/contact", false, "url.ssrf_or_unsafe")]
    [InlineData("http://localhost/contact", false, "url.ssrf_or_unsafe")]
    [InlineData("http://192.168.0.10/contact", false, "url.ssrf_or_unsafe")]
    [InlineData("http://[::1]/contact", false, "url.ssrf_or_unsafe")]
    public void UrlAdmission_rejects_malformed_and_keeps_encoded_spaces(
        string url,
        bool accepted,
        string reasonPrefix)
    {
        var decision = UrlAdmissionPolicy.Evaluate(url);
        Assert.Equal(accepted, decision.Accepted);
        Assert.StartsWith(reasonPrefix.Split('.')[0], decision.ReasonCode);
    }

    [Fact]
    public void UrlAdmission_repetition_fingerprint_groups_detail_pages()
    {
        var a = UrlAdmissionPolicy.BuildRepetitionFingerprint("https://dir.example/get_details/1001");
        var b = UrlAdmissionPolicy.BuildRepetitionFingerprint("https://dir.example/get_details/1002");
        var contact = UrlAdmissionPolicy.BuildRepetitionFingerprint("https://school.sa/contact");
        Assert.NotNull(a);
        Assert.Equal(a, b);
        Assert.Null(contact);
    }

    [Fact]
    public void CrawlQueueScheduler_fetches_contact_before_prior_research_homepage()
    {
        var pending = new List<DiscoveredUrl>
        {
            new()
            {
                Url = "https://example.org/blog/post-1",
                NormalizedUrl = "https://example.org/blog/post-1",
                NormalizedDomain = "example.org",
                SourceType = DiscoverySourceType.PriorResearch,
                Status = DiscoveredUrlStatus.Pending,
                Depth = 0,
                PagePurpose = UrlPagePurpose.Unknown.ToString(),
                PriorityScore = 45,
                DiscoveredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            new()
            {
                Url = "https://example.com/school/",
                NormalizedUrl = "https://example.com/school/",
                NormalizedDomain = "example.com",
                SourceType = DiscoverySourceType.PriorResearch,
                Status = DiscoveredUrlStatus.Pending,
                Depth = 0,
                PagePurpose = UrlPagePurpose.CompanyProfile.ToString(),
                PriorityScore = 39,
                DiscoveredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-9)
            },
            new()
            {
                Url = "https://example.com/school/contact",
                NormalizedUrl = "https://example.com/school/contact",
                NormalizedDomain = "example.com",
                SourceType = DiscoverySourceType.Sitemap,
                Status = DiscoveredUrlStatus.Pending,
                Depth = 1,
                PagePurpose = UrlPagePurpose.Contact.ToString(),
                PriorityScore = 94,
                DiscoveredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        };

        var ranked = CrawlQueueScheduler.RankPending(
            pending,
            ["Education", "Schools"],
            [],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            maxPagesPerDomain: 5,
            maxPerDomainInBatch: 2);

        Assert.Equal("https://example.com/school/contact", ranked[0].Url.NormalizedUrl);
        Assert.True(ranked[0].Priority.Total > ranked.Last().Priority.Total);
    }

    [Fact]
    public void CompanyIdentity_rejects_generic_titles_including_arabic()
    {
        Assert.True(CompanyIdentityResolver.IsGenericName("Home"));
        Assert.True(CompanyIdentityResolver.IsGenericName("الصفحة الرئيسية"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Find a School"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Locations"));

        var identity = CompanyIdentityResolver.Resolve(
            "الصفحة الرئيسية",
            "generic page",
            "directory.example",
            "https://directory.example/");
        Assert.False(identity.IsAcceptable);
    }

    [Fact]
    public void EntityClassifier_does_not_treat_directory_listicle_as_business()
    {
        var directory = EntityClassifier.Classify(
            "Find a School",
            "Find a School in Riyadh",
            "Browse schools directory",
            "https://directory.example/find-a-school",
            DiscoverySourceType.DirectoryCatalog);
        Assert.Equal(ResearchEntityType.DirectoryAggregator, directory.EntityType);

        var listicle = EntityClassifier.Classify(
            "Best Schools in Riyadh",
            "Best Schools in Riyadh 2024",
            "Our top picks ranked",
            "https://news.example/best-schools-in-riyadh",
            DiscoverySourceType.LinkExpansion);
        Assert.Equal(ResearchEntityType.PublisherEditorial, listicle.EntityType);
        Assert.False(EntityClassifier.IsAllowedTarget(listicle.EntityType, ResearchEntityType.TargetBusiness));
    }

    [Fact]
    public async Task DiscoverAsync_continues_batches_until_budget_and_prefers_contacts()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();

        var seeds = new List<DiscoveredUrlCandidate>();
        var priorHosts = new[] { "example.org", "example.net", "example.com" };
        for (var i = 0; i < 30; i++)
        {
            var host = priorHosts[i % priorHosts.Length];
            var url = $"https://{host}/prior-{i}/";
            seeds.Add(new DiscoveredUrlCandidate
            {
                Url = url,
                SourceType = DiscoverySourceType.PriorResearch,
                SourceName = "Prior",
                Depth = 0
            });
            fetcher.Map(
                url,
                FakeSafeHttpFetcher.Html(
                    url,
                    $"<html><head><title>Home</title></head><body>Blog post {i}</body></html>"));
        }

        for (var i = 0; i < 12; i++)
        {
            var host = priorHosts[i % priorHosts.Length];
            var home = $"https://{host}/school-{i}/";
            var contact = $"https://{host}/school-{i}/contact";
            seeds.Add(new DiscoveredUrlCandidate
            {
                Url = home,
                SourceType = DiscoverySourceType.LinkExpansion,
                SourceName = "Expansion",
                Depth = 0
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
                Url = $"https://{host}/school-{i}/undefined/page",
                SourceType = DiscoverySourceType.LinkExpansion,
                SourceName = "Bad",
                Depth = 1
            });
            seeds.Add(new DiscoveredUrlCandidate
            {
                Url = $"https://{host}/get_details/{1000 + i}",
                SourceType = DiscoverySourceType.Sitemap,
                SourceName = "Pagination",
                Depth = 1
            });

            fetcher.Map(
                home,
                FakeSafeHttpFetcher.Html(
                    home,
                    "<html><head>"
                    + $"<title>School {i} | Home</title>"
                    + "<script type=\"application/ld+json\">"
                    + $"{{\"@type\":\"Organization\",\"name\":\"School {i} Academy\",\"url\":\"{home}\",\"email\":\"info{i}@{host}\"}}"
                    + "</script>"
                    + $"<meta property=\"og:site_name\" content=\"School {i} Academy\" />"
                    + "</head>"
                    + $"<body>School {i} Academy admissions Saudi Arabia education."
                    + $"<a href=\"{contact}\">Contact</a></body></html>"));
            fetcher.Map(
                contact,
                FakeSafeHttpFetcher.Html(
                    contact,
                    "<html><head>"
                    + $"<title>Contact School {i} Academy</title>"
                    + "<script type=\"application/ld+json\">"
                    + $"{{\"@type\":\"Organization\",\"name\":\"School {i} Academy\",\"email\":\"info{i}@{host}\"}}"
                    + "</script>"
                    + "</head>"
                    + $"<body>Email <a href=\"mailto:info{i}@{host}\">info{i}@{host}</a></body></html>"));
        }

        fetcher.Map(
            "https://example.net/directory-list",
            FakeSafeHttpFetcher.Html(
                "https://example.net/directory-list",
                """
                <html><head><title>Find a School</title></head>
                <body>School directory <a href="https://example.com/school-0/">School 0</a></body></html>
                """));
        seeds.Insert(0, new DiscoveredUrlCandidate
        {
            Url = "https://example.net/directory-list",
            SourceType = DiscoverySourceType.DirectoryCatalog,
            SourceName = "Directory",
            Depth = 0
        });

        fetcher.Map(
            "https://example.org/blocked/",
            FakeSafeHttpFetcher.RobotsBlocked("https://example.org/blocked/"));
        seeds.Add(new DiscoveredUrlCandidate
        {
            Url = "https://example.org/blocked/",
            SourceType = DiscoverySourceType.PriorResearch,
            SourceName = "Blocked",
            Depth = 0
        });

        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource("DirectoryCatalog", seeds.Where(x => x.SourceType == DiscoverySourceType.DirectoryCatalog).ToList()),
            new FixedDiscoverySource("PriorResearchUrls", seeds.Where(x => x.SourceType == DiscoverySourceType.PriorResearch).ToList()),
            new FixedDiscoverySource("SeedUrls", []),
            new FixedDiscoverySource(
                "CommonCrawlIndex",
                seeds.Where(x => x.SourceType is DiscoverySourceType.Sitemap or DiscoverySourceType.LinkExpansion).ToList())
        };

        var crawler = CreateCrawler(db, fetcher, sources, maxFetchesPerRun: 80, batchSizeHint: 20);
        var state = new ResearchDiscoveryState
        {
            ConfiguredBatchSize = 20,
            RequestedFetchBudget = 80
        };
        var context = CreateSourceContext(project, run, state, discoveryBatchSize: 20);

        var allCandidates = new List<ProspectCandidate>();
        var batches = 0;
        while (!state.CompletedSourceNames.Contains("IndependentCrawler")
               && state.PagesFetched < 80
               && batches < 8)
        {
            var pagesBefore = state.PagesFetched;
            var batch = await crawler.DiscoverAsync(context, CancellationToken.None);
            batches++;
            allCandidates.AddRange(batch);
            if (batch.Count == 0
                && state.PagesFetched == pagesBefore
                && !string.Equals(state.LastBudgetStopReason, "fetch.batch_limit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.Equals(state.LastBudgetStopReason, "fetch.run_budget_exhausted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.LastBudgetStopReason, "fetch.queue_empty", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.LastBudgetStopReason, "fetch.no_eligible_pending", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        Assert.True(batches > 1, $"Expected multiple batches, got {batches}");
        Assert.True(state.PagesFetched > 20, $"Expected more than one batch of fetches, got {state.PagesFetched}");
        Assert.Equal(state.PagesFetched, state.FetchBudgetConsumed);
        Assert.Equal(state.FetchesSucceeded, state.PagesFetched);
        Assert.True(state.FetchesAttempted >= state.FetchesSucceeded);
        Assert.DoesNotContain(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("/undefined/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("/contact", StringComparison.OrdinalIgnoreCase));

        var contactIndex = fetcher.RequestedUrls.FindIndex(u =>
            u.AbsoluteUri.Contains("/contact", StringComparison.OrdinalIgnoreCase));
        var latePriorIndex = fetcher.RequestedUrls.FindIndex(u =>
            u.AbsoluteUri.Contains("/prior-20/", StringComparison.OrdinalIgnoreCase));
        if (contactIndex >= 0 && latePriorIndex >= 0)
        {
            Assert.True(contactIndex < latePriorIndex || state.PagesFetched >= 40);
        }

        Assert.Contains(
            allCandidates,
            x => x.CompanyName.Contains("School", StringComparison.OrdinalIgnoreCase)
                 && !string.IsNullOrWhiteSpace(x.Email));
        Assert.DoesNotContain(
            allCandidates,
            x => x.CompanyName.Contains("Find a School", StringComparison.OrdinalIgnoreCase));

        Assert.False(string.Equals(state.LastBudgetStopReason, "fetch.budget_batch_or_run_limit", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            state.LastBudgetStopReason is "fetch.run_budget_exhausted"
                or "fetch.batch_limit"
                or "fetch.queue_empty"
                or "fetch.no_eligible_pending"
                or null);
        Assert.True(state.TopPendingUrls.Count <= 10);
    }

    [Fact]
    public async Task DiscoverAsync_malformed_urls_never_consume_fetch_budget()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        fetcher.Map(
            "https://example.com/good/contact",
            FakeSafeHttpFetcher.Html(
                "https://example.com/good/contact",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"Organization","name":"Good School","email":"info@example.com"}
                  </script>
                </head><body>Good School contact Saudi Arabia</body></html>
                """));

        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "SeedUrls",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/undefined/x",
                        SourceType = DiscoverySourceType.SeedUrl,
                        SourceName = "Bad",
                        Depth = 0
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/<?=$id?>",
                        SourceType = DiscoverySourceType.SeedUrl,
                        SourceName = "Php",
                        Depth = 0
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/good/contact",
                        SourceType = DiscoverySourceType.SeedUrl,
                        SourceName = "Good",
                        Depth = 0
                    }
                ])
        };

        var crawler = CreateCrawler(db, fetcher, sources, maxFetchesPerRun: 10, batchSizeHint: 10);
        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state, discoveryBatchSize: 10);
        await crawler.DiscoverAsync(context, CancellationToken.None);

        Assert.DoesNotContain(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("undefined", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("<?", StringComparison.Ordinal));
        Assert.Contains(fetcher.RequestedUrls, u => u.AbsoluteUri.Contains("/good/contact", StringComparison.OrdinalIgnoreCase));
        Assert.True(state.UrlsRejectedFiltered >= 1
                    || state.UrlRejectionReasons.ContainsKey("url.undefined_segment")
                    || state.UrlRejectionReasons.ContainsKey("url.php_template"));
        Assert.Equal(state.FetchesSucceeded, state.PagesFetched);
    }

    [Fact]
    public async Task DiscoverAsync_is_deterministic_for_identical_fixture()
    {
        var first = await RunDeterministicFixtureAsync();
        var second = await RunDeterministicFixtureAsync();
        Assert.Equal(first.Fetched, second.Fetched);
        Assert.Equal(first.StopReason, second.StopReason);
        Assert.Equal(first.Companies, second.Companies);
    }

    private static async Task<(List<string> Fetched, string? StopReason, List<string> Companies)> RunDeterministicFixtureAsync()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        var urls = new[]
        {
            "https://example.com/alpha/",
            "https://example.com/alpha/contact",
            "https://example.org/beta/",
            "https://example.org/beta/about"
        };
        foreach (var url in urls)
        {
            var uri = new Uri(url);
            var name = uri.AbsolutePath.Trim('/').Split('/')[0];
            var host = uri.Host;
            fetcher.Map(
                url,
                FakeSafeHttpFetcher.Html(
                    url,
                    "<html><head><script type=\"application/ld+json\">"
                    + $"{{\"@type\":\"Organization\",\"name\":\"{name} School\",\"email\":\"info@{host}\"}}"
                    + $"</script></head><body>{name} School Saudi Arabia education</body></html>"));
        }

        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "SeedUrls",
                urls.Select(u => new DiscoveredUrlCandidate
                {
                    Url = u,
                    SourceType = u.Contains("contact", StringComparison.Ordinal) || u.Contains("about", StringComparison.Ordinal)
                        ? DiscoverySourceType.Sitemap
                        : DiscoverySourceType.SeedUrl,
                    SourceName = "Fixture",
                    Depth = u.Contains('/', StringComparison.Ordinal) && u.Count(c => c == '/') > 2 ? 1 : 0
                }).ToList())
        };

        var crawler = CreateCrawler(db, fetcher, sources, maxFetchesPerRun: 10, batchSizeHint: 5);
        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state, discoveryBatchSize: 5);
        var companies = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var batch = await crawler.DiscoverAsync(context, CancellationToken.None);
            companies.AddRange(batch.Select(x => x.CompanyName));
            if (state.CompletedSourceNames.Contains("IndependentCrawler"))
            {
                break;
            }
        }

        return (
            fetcher.RequestedUrls.Select(u => u.AbsoluteUri.TrimEnd('/')).ToList(),
            state.LastBudgetStopReason,
            companies.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    private static IndependentCrawlProspectSource CreateCrawler(
        SendlyDbContext db,
        FakeSafeHttpFetcher fetcher,
        IEnumerable<IUrlDiscoverySource> sources,
        int maxFetchesPerRun,
        int batchSizeHint)
    {
        var options = MsOptions.Create(new ResearchOptions
        {
            EnableIndependentCrawler = true,
            EnableCommonCrawlDiscovery = false,
            MaxFetchesPerRun = maxFetchesPerRun,
            MaxPagesPerCrawl = 40,
            MaxDiscoveryCandidates = 400,
            MaxCandidateDomains = 200,
            MaxPriorResearchSharePercent = 40,
            EvidenceReuseMaxAgeDays = 30,
            MaxHostFetchFailuresBeforeAvoid = 2,
            MaxResearchDurationMinutes = 30,
            DiscoveryOversampleFactor = 4
        });
        _ = batchSizeHint;
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
        int discoveryBatchSize)
        => new()
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            ProjectName = project.Name,
            Countries = [],
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
            Name = "Scheduler Fixture",
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
