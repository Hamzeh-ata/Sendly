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

public class RecoveryStarvationAndSsrfRegressionTests
{
    [Fact]
    public void ContinuationPolicy_defers_recovery_when_eligible_urls_are_schedulable()
    {
        var state = new ResearchDiscoveryState
        {
            PagesFetched = 20,
            FetchBudgetLimit = 80,
            YieldRecoveryAttempted = false
        };

        Assert.True(ResearchRunContinuationPolicy.ShouldDeferYieldRecoveryForQueuedWork(state, 12));
        Assert.False(ResearchRunContinuationPolicy.ShouldDeferYieldRecoveryForQueuedWork(state, 0));
    }

    [Fact]
    public async Task Second_batch_fetches_high_score_contacts_before_recovery_rediscovery()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        var recoveryProbe = new ProbeDiscoverySource("CommonCrawlIndex");

        for (var i = 0; i < 18; i++)
        {
            var host = i % 2 == 0 ? "example.org" : "example.net";
            var url = $"https://{host}/prior-home-{i}/";
            fetcher.Map(
                url,
                FakeSafeHttpFetcher.Html(url, $"<html><head><title>Home {i}</title></head><body>Prior blog</body></html>"));
            db.DiscoveredUrls.Add(new DiscoveredUrl
            {
                ResearchRunId = run.Id,
                ProjectId = project.Id,
                Url = url,
                NormalizedUrl = url.TrimEnd('/'),
                NormalizedDomain = host,
                SourceType = DiscoverySourceType.PriorResearch,
                SourceName = "Prior",
                Status = DiscoveredUrlStatus.Pending,
                Depth = 0,
                PagePurpose = UrlPagePurpose.CompanyProfile.ToString(),
                PriorityScore = 41,
                DiscoveredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            });
        }

        for (var i = 0; i < 12; i++)
        {
            var host = $"school-{i}.example.com";
            var contact = $"https://{host}/contact";
            fetcher.Map(
                contact,
                FakeSafeHttpFetcher.Html(
                    contact,
                    "<html><head>"
                    + $"<title>Contact School {i}</title>"
                    + "<script type=\"application/ld+json\">"
                    + $"{{\"@type\":\"Organization\",\"name\":\"School {i} Academy\",\"email\":\"info@{host}\"}}"
                    + "</script></head>"
                    + $"<body>Email <a href=\"mailto:info@{host}\">info@{host}</a></body></html>"));
            db.DiscoveredUrls.Add(new DiscoveredUrl
            {
                ResearchRunId = run.Id,
                ProjectId = project.Id,
                Url = contact,
                NormalizedUrl = contact,
                NormalizedDomain = host,
                SourceType = DiscoverySourceType.Sitemap,
                SourceName = "Sitemap",
                Status = DiscoveredUrlStatus.Pending,
                Depth = 1,
                PagePurpose = UrlPagePurpose.Contact.ToString(),
                PriorityScore = 106,
                DiscoveredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
        }

        db.DiscoveredUrls.Add(new DiscoveredUrl
        {
            ResearchRunId = run.Id,
            ProjectId = project.Id,
            Url = "http://127.0.1.1/ar/contact",
            NormalizedUrl = "http://127.0.1.1/ar/contact",
            NormalizedDomain = "127.0.1.1",
            SourceType = DiscoverySourceType.Sitemap,
            SourceName = "Sitemap",
            Status = DiscoveredUrlStatus.Pending,
            Depth = 1,
            PagePurpose = UrlPagePurpose.Contact.ToString(),
            PriorityScore = 94,
            DiscoveredAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var crawler = CreateCrawler(
            db,
            fetcher,
            [recoveryProbe, new FixedDiscoverySource("PriorResearchUrls", [])],
            maxFetchesPerRun: 80,
            batchSizeHint: 20);

        var state = new ResearchDiscoveryState
        {
            IndependentDiscoverySeeded = true,
            PagesFetched = 20,
            FetchBudgetConsumed = 20,
            FetchBudgetLimit = 80,
            RequestedFetchBudget = 80,
            ConfiguredBatchSize = 20,
            UsefulPendingUrls = 30,
            PagesQueued = 31,
            CompaniesIdentified = 4,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        var context = new ProspectSourceContext
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            ProjectName = project.Name,
            Countries = [],
            Industries = ["Education", "Schools"],
            DiscoveryBatchSize = 20,
            DiscoveryState = state
        };

        var batch = await crawler.DiscoverAsync(context, CancellationToken.None);

        Assert.Equal(0, recoveryProbe.DiscoverCalls);
        Assert.False(state.YieldRecoveryAttempted);
        Assert.Contains(
            state.UrlRejectionReasons.Keys,
            k => k.Contains("ssrf", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            fetcher.RequestedUrls,
            u => u.Host.Equals("127.0.1.1", StringComparison.OrdinalIgnoreCase)
                 || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

        var unsafePending = await db.DiscoveredUrls.CountAsync(x =>
            x.ResearchRunId == run.Id
            && x.Url.Contains("127.0.1.1", StringComparison.Ordinal)
            && x.Status == DiscoveredUrlStatus.Pending);
        Assert.Equal(0, unsafePending);

        Assert.Contains(
            fetcher.RequestedUrls,
            u => u.AbsoluteUri.Contains("/contact", StringComparison.OrdinalIgnoreCase));

        var firstContact = fetcher.RequestedUrls.FindIndex(u =>
            u.AbsoluteUri.Contains("/contact", StringComparison.OrdinalIgnoreCase));
        var firstPrior = fetcher.RequestedUrls.FindIndex(u =>
            u.AbsoluteUri.Contains("/prior-home-", StringComparison.OrdinalIgnoreCase));
        Assert.True(firstContact >= 0, "Expected contact pages to be fetched");
        if (firstPrior >= 0)
        {
            Assert.True(
                firstContact < firstPrior,
                $"Contact fetch index {firstContact} should precede prior-home index {firstPrior}");
        }

        Assert.True(state.PagesFetched > 20, $"Expected additional fetches, got {state.PagesFetched}");
        Assert.Equal(state.PagesFetched, state.FetchBudgetConsumed);
        Assert.True(
            state.LastBudgetStopReason is null
            || state.LastBudgetStopReason is "fetch.batch_limit"
                or "fetch.queue_empty"
                or "fetch.no_eligible_pending"
                or "fetch.run_budget_exhausted",
            $"Unexpected stop reason: {state.LastBudgetStopReason}");
        _ = batch;
    }

    [Fact]
    public async Task Zero_eligible_queue_allows_bounded_recovery_then_returns()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        var recoveryProbe = new ProbeDiscoverySource("CommonCrawlIndex");

        var crawler = CreateCrawler(
            db,
            fetcher,
            [recoveryProbe],
            maxFetchesPerRun: 80,
            batchSizeHint: 20);

        var state = new ResearchDiscoveryState
        {
            IndependentDiscoverySeeded = true,
            PagesFetched = 20,
            FetchBudgetConsumed = 20,
            FetchBudgetLimit = 80,
            YieldRecoveryAttempted = false,
            CompaniesIdentified = 2,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        var context = new ProspectSourceContext
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            ProjectName = project.Name,
            Countries = [],
            Industries = ["Education"],
            DiscoveryBatchSize = 20,
            DiscoveryState = state
        };

        await crawler.DiscoverAsync(context, CancellationToken.None);

        Assert.True(state.YieldRecoveryAttempted);
        Assert.True(recoveryProbe.DiscoverCalls >= 1);
        Assert.True(
            state.StageDurationsMs.ContainsKey("yieldRecovery")
            || state.RecoveryPasses >= 1
            || state.LastBudgetStopReason is not null);
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
            EnableCommonCrawlDiscovery = true,
            MaxFetchesPerRun = maxFetchesPerRun,
            MaxPagesPerCrawl = 40,
            MaxDiscoveryCandidates = 400,
            MaxCandidateDomains = 200,
            MaxPriorResearchSharePercent = 40,
            EvidenceReuseMaxAgeDays = 30,
            MaxHostFetchFailuresBeforeAvoid = 2,
            MaxResearchDurationMinutes = 30,
            DiscoveryOversampleFactor = 4,
            ProviderTimeoutSeconds = 5
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
            Name = "Recovery Starvation Fixture",
            Description = "Education",
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
            RequestedProspectCount = 80,
            IncludeEmail = true,
            MinQualificationScore = 70,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            QualifiedCount = 0
        };
        db.ResearchRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private sealed class ProbeDiscoverySource : IUrlDiscoverySource
    {
        public ProbeDiscoverySource(string name) => Name = name;

        public string Name { get; }

        public int DiscoverCalls { get; private set; }

        public bool IsEnabled(UrlDiscoveryContext context) => true;

        public Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
            UrlDiscoveryContext context,
            CancellationToken cancellationToken)
        {
            DiscoverCalls++;
            return Task.FromResult<IReadOnlyList<DiscoveredUrlCandidate>>([]);
        }
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
