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

public class SearchLimitCrawlContinuationRegressionTests
{
    [Fact]
    public void ContinuationPolicy_search_exhaustion_does_not_block_eligible_crawl()
    {
        var run = new ResearchRun { RequestedProspectCount = 80, QualifiedCount = 0 };
        var state = new ResearchDiscoveryState
        {
            LimitsExhausted = true,
            LimitsExhaustedReason = "Maximum search requests reached",
            SearchLimitsExhausted = true,
            SearchLimitsExhaustedReason = "Maximum search requests reached",
            PagesFetched = 20,
            FetchBudgetLimit = 80,
            PagesQueued = 255,
            UsefulPendingUrls = 255,
            MoreEligibleWorkRemained = true,
            LastBudgetStopReason = ResearchRunContinuationPolicy.FetchBatchLimitReason
        };

        Assert.True(
            ResearchRunContinuationPolicy.ShouldContinueResearch(run, state, maxResearchDurationMinutes: 30));
        Assert.True(ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(state));
        Assert.True(ResearchRunContinuationPolicy.ShouldDeferSearchForIndependentCrawl(state));
        Assert.True(
            ResearchRunContinuationPolicy.ShouldContinueAfterEmptyProspectBatch(state, 0, 260));
    }

    [Fact]
    public void ContinuationPolicy_duration_and_empty_queue_still_terminate()
    {
        var run = new ResearchRun { RequestedProspectCount = 80, QualifiedCount = 0 };
        var durationState = new ResearchDiscoveryState
        {
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-45),
            PagesFetched = 20,
            FetchBudgetLimit = 80,
            PagesQueued = 10,
            UsefulPendingUrls = 10
        };
        Assert.False(
            ResearchRunContinuationPolicy.ShouldContinueResearch(run, durationState, maxResearchDurationMinutes: 30));

        var emptyState = new ResearchDiscoveryState
        {
            PagesFetched = 20,
            FetchBudgetLimit = 80,
            PagesQueued = 0,
            UsefulPendingUrls = 0,
            MoreEligibleWorkRemained = false,
            LastBudgetStopReason = "fetch.queue_empty"
        };
        Assert.False(
            ResearchRunContinuationPolicy.ShouldContinueAfterEmptyProspectBatch(emptyState, 20, 0));
    }

    [Fact]
    public async Task Orchestration_search_exhaustion_allows_multiple_crawl_batches_and_prefers_contacts()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();

        var seeds = new List<DiscoveredUrlCandidate>();
        var priorHosts = new[] { "example.org", "example.net", "example.com", "example.edu" };
        for (var i = 0; i < 24; i++)
        {
            var host = priorHosts[i % priorHosts.Length];
            var home = $"https://{host}/prior-{i}/";
            seeds.Add(new DiscoveredUrlCandidate
            {
                Url = home,
                SourceType = DiscoverySourceType.PriorResearch,
                SourceName = "Prior",
                Depth = 0
            });
            fetcher.Map(
                home,
                FakeSafeHttpFetcher.Html(
                    home,
                    $"<html><head><title>Prior {i}</title></head><body>Blog listing {i}</body></html>"));
        }

        for (var i = 0; i < 16; i++)
        {
            var host = priorHosts[i % priorHosts.Length];
            var home = $"https://{host}/campus-{i}/";
            var contact = $"https://{host}/campus-{i}/contact";
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
                Url = $"https://{host}/campus-{i}/undefined/page",
                SourceType = DiscoverySourceType.LinkExpansion,
                SourceName = "Bad",
                Depth = 1
            });

            fetcher.Map(
                home,
                FakeSafeHttpFetcher.Html(
                    home,
                    "<html><head>"
                    + $"<title>Campus {i} Academy</title>"
                    + "<script type=\"application/ld+json\">"
                    + $"{{\"@type\":\"Organization\",\"name\":\"Campus {i} Academy\",\"url\":\"{home}\",\"email\":\"info{i}@{host}\"}}"
                    + "</script></head>"
                    + $"<body>Campus {i} Academy Saudi Arabia education."
                    + $"<a href=\"{contact}\">Contact</a></body></html>"));
            fetcher.Map(
                contact,
                FakeSafeHttpFetcher.Html(
                    contact,
                    "<html><head>"
                    + $"<title>Contact Campus {i} Academy</title>"
                    + "<script type=\"application/ld+json\">"
                    + $"{{\"@type\":\"Organization\",\"name\":\"Campus {i} Academy\",\"email\":\"info{i}@{host}\"}}"
                    + "</script></head>"
                    + $"<body>Email <a href=\"mailto:info{i}@{host}\">info{i}@{host}</a></body></html>"));
        }

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
        fetcher.Map(
            "https://example.net/down/",
            FakeSafeHttpFetcher.Forbidden("https://example.net/down/"));
        seeds.Add(new DiscoveredUrlCandidate
        {
            Url = "https://example.net/down/",
            SourceType = DiscoverySourceType.PriorResearch,
            SourceName = "Failed",
            Depth = 0
        });

        var discoverySources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "PriorResearchUrls",
                seeds.Where(x => x.SourceType == DiscoverySourceType.PriorResearch).ToList()),
            new FixedDiscoverySource(
                "CommonCrawlIndex",
                seeds.Where(x => x.SourceType is DiscoverySourceType.Sitemap or DiscoverySourceType.LinkExpansion)
                    .ToList())
        };

        var crawler = CreateCrawler(db, fetcher, discoverySources, maxFetchesPerRun: 80, batchSizeHint: 20);
        var search = new ExhaustingSearchProspectSource(maxRequestsBeforeExhaust: 2);
        var sources = new IProspectSource[] { crawler, search };

        var state = new ResearchDiscoveryState
        {
            ConfiguredBatchSize = 20,
            RequestedFetchBudget = 80,
            FetchBudgetLimit = 80,
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

        var batches = 0;
        while (batches < 12)
        {
            var canContinue = ResearchRunContinuationPolicy.ShouldContinueResearch(run, state, 30)
                              || (state.CompletedSourceNames.Contains("IndependentCrawler")
                                  && !search.Exhausted
                                  && !state.CompletedSourceNames.Contains(
                                      ResearchRunContinuationPolicy.WebSearchSourceName));
            if (!canContinue)
            {
                break;
            }

            var pagesBefore = state.PagesFetched;
            var pendingBefore = state.PagesQueued;
            state.BatchesStarted++;
            var batch = await DiscoverLikeProcessorAsync(sources, context, CancellationToken.None);
            state.BatchesCompleted++;
            batches++;

            if (batch.Count == 0
                && !ResearchRunContinuationPolicy.ShouldContinueAfterEmptyProspectBatch(
                    state,
                    pagesBefore,
                    pendingBefore)
                && (search.Exhausted || state.CompletedSourceNames.Contains("IndependentCrawler")))
            {
                if (state.CompletedSourceNames.Contains("IndependentCrawler") && !search.Exhausted)
                {
                    continue;
                }

                break;
            }

            if (state.CompletedSourceNames.Contains("IndependentCrawler")
                && search.Exhausted
                && !ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(state))
            {
                break;
            }
        }

        Assert.True(state.BatchesStarted > 1, $"Expected multiple batches, got {state.BatchesStarted}");
        Assert.True(state.PagesFetched > 20, $"Expected more than one batch of fetches, got {state.PagesFetched}");
        Assert.True(state.PagesFetched <= 80);
        Assert.Equal(state.PagesFetched, state.FetchBudgetConsumed);
        Assert.True(state.FetchesSucceeded <= state.FetchesAttempted);
        Assert.True(search.Exhausted, "Search provider should record exhaustion");
        Assert.True(state.SearchLimitsExhausted);
        Assert.Equal("Maximum search requests reached", state.SearchLimitsExhaustedReason);
        Assert.False(
            ResearchRunContinuationPolicy.IsSearchOnlyLimitReason(state.LimitsExhaustedReason)
            && ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(state),
            "Search exhaustion must not terminate crawl while eligible work remains");
        Assert.True(
            search.RequestsIssued <= 2,
            $"Search request limit exceeded: {search.RequestsIssued}");
        Assert.Contains(
            fetcher.RequestedUrls,
            u => u.AbsoluteUri.Contains("/contact", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            fetcher.RequestedUrls,
            u => u.AbsoluteUri.Contains("/undefined/", StringComparison.OrdinalIgnoreCase));

        var contactKeys = fetcher.RequestedUrls
            .Select(u => u.AbsoluteUri.TrimEnd('/'))
            .Where(u => u.Contains("/contact", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var maxContactRepeats = contactKeys
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();
        Assert.True(
            maxContactRepeats <= 2,
            $"Contact pages must not be re-queued across batches; max repeats={maxContactRepeats}");
        Assert.Equal(state.BatchesStarted, state.BatchesCompleted);
        Assert.True(state.PagesFetched <= state.FetchesAttempted);
    }

    [Fact]
    public async Task Orchestration_stops_when_no_eligible_pending_remains()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        fetcher.Map(
            "https://example.com/solo/",
            FakeSafeHttpFetcher.Html(
                "https://example.com/solo/",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"Organization","name":"Solo School","email":"info@example.com"}
                  </script>
                </head><body>Solo School education</body></html>
                """));

        var crawler = CreateCrawler(
            db,
            fetcher,
            [
                new FixedDiscoverySource(
                    "PriorResearchUrls",
                    [
                        new DiscoveredUrlCandidate
                        {
                            Url = "https://example.com/solo/",
                            SourceType = DiscoverySourceType.PriorResearch,
                            SourceName = "Prior",
                            Depth = 0
                        }
                    ])
            ],
            maxFetchesPerRun: 80,
            batchSizeHint: 20);
        var search = new ExhaustingSearchProspectSource(maxRequestsBeforeExhaust: 1);
        var sources = new IProspectSource[] { crawler, search };
        var state = new ResearchDiscoveryState
        {
            FetchBudgetLimit = 80,
            RequestedFetchBudget = 80,
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

        var batches = 0;
        while (ResearchRunContinuationPolicy.ShouldContinueResearch(run, state, 30) && batches < 6)
        {
            var pagesBefore = state.PagesFetched;
            var pendingBefore = state.PagesQueued;
            state.BatchesStarted++;
            var batch = await DiscoverLikeProcessorAsync(sources, context, CancellationToken.None);
            state.BatchesCompleted++;
            batches++;
            if (batch.Count == 0
                && !ResearchRunContinuationPolicy.ShouldContinueAfterEmptyProspectBatch(
                    state,
                    pagesBefore,
                    pendingBefore))
            {
                break;
            }

            if (state.CompletedSourceNames.Contains("IndependentCrawler")
                && !ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(state))
            {
                break;
            }
        }

        Assert.True(batches >= 1, $"Expected at least one batch, got {batches}; stop={state.LastBudgetStopReason}; queued={state.PagesQueued}");
        Assert.True(state.PagesFetched >= 1, $"Expected fetches, got {state.PagesFetched}");
        Assert.False(
            ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(state),
            "Coordinator must not keep scheduling once the queue is empty");
    }

    private static async Task<List<ProspectCandidate>> DiscoverLikeProcessorAsync(
        IEnumerable<IProspectSource> sources,
        ProspectSourceContext context,
        CancellationToken cancellationToken)
    {
        var batch = new List<ProspectCandidate>();
        foreach (var source in sources)
        {
            if (!source.IsEnabled)
            {
                continue;
            }

            if (context.DiscoveryState.CompletedSourceNames.Contains(source.Name))
            {
                continue;
            }

            if (string.Equals(
                    source.Name,
                    ResearchRunContinuationPolicy.WebSearchSourceName,
                    StringComparison.OrdinalIgnoreCase)
                && ResearchRunContinuationPolicy.ShouldDeferSearchForIndependentCrawl(
                    context.DiscoveryState))
            {
                continue;
            }

            var discovered = await source.DiscoverAsync(context, cancellationToken);
            batch.AddRange(discovered);
        }

        return ProspectCandidateDeduplicator.DeduplicateAndMerge(batch);
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
            Name = "Search Limit Fixture",
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
            RequestedProspectCount = 80,
            IncludeEmail = true,
            MinQualificationScore = 70,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.ResearchRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private sealed class ExhaustingSearchProspectSource : IProspectSource
    {
        private readonly int _maxRequestsBeforeExhaust;

        public ExhaustingSearchProspectSource(int maxRequestsBeforeExhaust)
        {
            _maxRequestsBeforeExhaust = maxRequestsBeforeExhaust;
        }

        public string Name => ResearchRunContinuationPolicy.WebSearchSourceName;

        public bool IsEnabled => true;

        public int RequestsIssued { get; private set; }

        public bool Exhausted { get; private set; }

        public Task<IReadOnlyList<ProspectCandidate>> DiscoverAsync(
            ProspectSourceContext context,
            CancellationToken cancellationToken)
        {
            var state = context.DiscoveryState;
            if (state.CompletedSourceNames.Contains(Name) || Exhausted)
            {
                return Task.FromResult<IReadOnlyList<ProspectCandidate>>([]);
            }

            RequestsIssued++;
            state.SearchRequestsSuccessful++;
            state.SearchQueriesExecuted++;
            if (RequestsIssued >= _maxRequestsBeforeExhaust)
            {
                Exhausted = true;
                state.CompletedSourceNames.Add(Name);
                state.SearchLimitsExhausted = true;
                state.SearchLimitsExhaustedReason = "Maximum search requests reached";
            }

            return Task.FromResult<IReadOnlyList<ProspectCandidate>>([]);
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
