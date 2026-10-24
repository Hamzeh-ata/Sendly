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

public class UsSchoolLiveDiagnosticTests
{
    [Fact]
    public async Task Live_bounded_us_school_directory_finds_identifiable_us_candidates()
    {
        await using var db = CreateDb();
        var project = new OutreachProject
        {
            Name = "School Watch - USA Diagnostic",
            Description = "Bounded live diagnostic for US private schools"
        };
        db.OutreachProjects.Add(project);
        await db.SaveChangesAsync();

        var run = new ResearchRun
        {
            ProjectId = project.Id,
            Status = ResearchRunStatus.Running,
            RequestedProspectCount = 5,
            IncludeEmail = true,
            MinQualificationScore = 60,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.ResearchRuns.Add(run);
        await db.SaveChangesAsync();

        using var fetcher = new LiveHttpFetcher();
        var sources = new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "DirectoryCatalog",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://www.nais.org/school-directory",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "NAIS Independent Schools",
                        Depth = 0,
                        PriorityBoost = 25
                    },
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://www.privateschoolreview.com/california",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "Private School Review",
                        Depth = 0,
                        PriorityBoost = 20
                    }
                ])
        };

        var crawler = CreateCrawler(db, fetcher, sources, maxFetches: 12);
        var state = new ResearchDiscoveryState();
        var context = new ProspectSourceContext
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            ProjectName = project.Name,
            Countries = ["United States"],
            Industries = ["Private School", "Independent School"],
            Keywords = ["private school", "independent school"],
            DiscoveryBatchSize = 6,
            DiscoveryState = state
        };

        var candidates = new List<ProspectCandidate>();
        for (var i = 0; i < 3; i++)
        {
            var batch = await crawler.DiscoverAsync(context, CancellationToken.None);
            candidates.AddRange(batch);
        }

        var crawled = await db.CrawledPages.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        var discovered = await db.DiscoveredUrls.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        var events = await db.ResearchRunEvents.Where(x => x.ResearchRunId == run.Id).ToListAsync();

        Assert.Contains(crawled, x =>
            (x.FinalUrl ?? x.Url).Contains("nais.org/school-directory", StringComparison.OrdinalIgnoreCase)
            || (x.FinalUrl ?? x.Url).Contains("privateschoolreview.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fetcher.RequestedUrls, u =>
            u.Host.StartsWith("sso.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fetcher.RequestedUrls, u =>
            u.Host.Contains("closeup.org", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fetcher.RequestedUrls, u =>
            u.AbsoluteUri.Contains("/vn/", StringComparison.OrdinalIgnoreCase)
            || u.AbsoluteUri.Contains("/sg/", StringComparison.OrdinalIgnoreCase)
            || u.AbsoluteUri.Contains("/my/", StringComparison.OrdinalIgnoreCase));

        var listingEvents = events.Count(x => x.EventType == ResearchRunEventType.ListingOrganizationExtracted);
        var schoolishCandidates = candidates
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.CompanyName)
                && !CompanyIdentityResolver.IsGenericName(x.CompanyName)
                && (x.CompanyName.Contains("School", StringComparison.OrdinalIgnoreCase)
                    || x.CompanyName.Contains("Academy", StringComparison.OrdinalIgnoreCase)
                    || x.CompanyName.Contains("Prep", StringComparison.OrdinalIgnoreCase)
                    || x.Website?.Contains(".edu", StringComparison.OrdinalIgnoreCase) == true
                    || x.Website?.Contains("school", StringComparison.OrdinalIgnoreCase) == true))
            .GroupBy(x => x.CompanyName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        Assert.True(
            listingEvents > 0 || schoolishCandidates.Count > 0 || discovered.Any(x =>
                x.Url.Contains("/school-directory/school/", StringComparison.OrdinalIgnoreCase)
                || x.Url.Contains("-profile", StringComparison.OrdinalIgnoreCase)),
            $"Diagnostic did not observe school listings or candidates. fetched={fetcher.RequestedUrls.Count}, crawled={crawled.Count}, listingEvents={listingEvents}, candidates={candidates.Count}");

        Assert.True(
            schoolishCandidates.Count > 0
            || discovered.Count(x =>
                x.Url.Contains("/school-directory/school/", StringComparison.OrdinalIgnoreCase)
                || x.Url.Contains("-profile", StringComparison.OrdinalIgnoreCase)) >= 3,
            "Expected identifiable US school profile URLs or extracted school candidates.");
    }

    private static IndependentCrawlProspectSource CreateCrawler(
        SendlyDbContext db,
        ISafeHttpFetcher fetcher,
        IEnumerable<IUrlDiscoverySource> sources,
        int maxFetches)
    {
        var options = MsOptions.Create(new ResearchOptions
        {
            EnableIndependentCrawler = true,
            EnableCommonCrawlDiscovery = false,
            MaxFetchesPerRun = maxFetches,
            MaxPagesPerCrawl = 4,
            MaxDiscoveryCandidates = 60,
            MaxPriorResearchSharePercent = 40,
            EvidenceReuseMaxAgeDays = 30,
            MaxHostFetchFailuresBeforeAvoid = 2,
            MaxResearchDurationMinutes = 10
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

    private static SendlyDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SendlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SendlyDbContext(options);
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
