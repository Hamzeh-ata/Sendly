using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using Sendly.Infrastructure.Research;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sendly.Application.Tests;

public class AutomaticDiscoveryCoordinatorTests
{
    [Fact]
    public async Task DiscoverAsync_DeduplicatesAcrossProviders()
    {
        var sources = new IUrlDiscoverySource[]
        {
            new FakeDiscoverySource(
                "A",
                enabled: true,
                [
                    Candidate("https://example.com/company-a"),
                    Candidate("https://example.com/company-b")
                ]),
            new FakeDiscoverySource(
                "B",
                enabled: true,
                [
                    Candidate("https://example.com/company-a"),
                    Candidate("https://example.com/company-c")
                ])
        };

        var coordinator = new AutomaticDiscoveryCoordinator(
            sources,
            NullLogger<AutomaticDiscoveryCoordinator>.Instance);

        var result = await coordinator.DiscoverAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(3, result.Candidates.Count);
        Assert.Equal(1, result.DuplicatesRemoved);
        Assert.Equal(2, result.ProviderStats.Count(x => x.Succeeded));
        Assert.Contains("3 candidate", result.Summary);
    }

    [Fact]
    public async Task DiscoverAsync_ContinuesWhenOneProviderFails()
    {
        var sources = new IUrlDiscoverySource[]
        {
            new ThrowingDiscoverySource("Failing"),
            new FakeDiscoverySource(
                "Working",
                enabled: true,
                [Candidate("https://example.com/ok")])
        };

        var coordinator = new AutomaticDiscoveryCoordinator(
            sources,
            NullLogger<AutomaticDiscoveryCoordinator>.Instance);

        var result = await coordinator.DiscoverAsync(CreateContext(), CancellationToken.None);

        Assert.Single(result.Candidates);
        Assert.Contains(result.ProviderStats, x => x.ProviderName == "Failing" && x.Attempted && !x.Succeeded);
        Assert.Contains(result.ProviderStats, x => x.ProviderName == "Working" && x.Succeeded);
    }

    [Fact]
    public async Task DiscoverAsync_EmptyResults_ReportsAccurateSummary()
    {
        var sources = new IUrlDiscoverySource[]
        {
            new FakeDiscoverySource("Empty", enabled: true, [])
        };

        var coordinator = new AutomaticDiscoveryCoordinator(
            sources,
            NullLogger<AutomaticDiscoveryCoordinator>.Instance);

        var result = await coordinator.DiscoverAsync(CreateContext(), CancellationToken.None);

        Assert.Empty(result.Candidates);
        Assert.Contains("no candidates", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoverAsync_NoEligibleProviders_ReportsNoProvidersEligible()
    {
        var sources = new IUrlDiscoverySource[]
        {
            new FakeDiscoverySource("Disabled", enabled: false, [Candidate("https://example.com/x")])
        };

        var coordinator = new AutomaticDiscoveryCoordinator(
            sources,
            NullLogger<AutomaticDiscoveryCoordinator>.Instance);

        var result = await coordinator.DiscoverAsync(CreateContext(), CancellationToken.None);

        Assert.Empty(result.Candidates);
        Assert.Contains("No automatic discovery providers were eligible", result.Summary);
    }

    [Fact]
    public async Task DiscoverAsync_RejectsUnsafeUrls()
    {
        var sources = new IUrlDiscoverySource[]
        {
            new FakeDiscoverySource(
                "Unsafe",
                enabled: true,
                [
                    Candidate("http://localhost/admin"),
                    Candidate("https://example.com/safe")
                ])
        };

        var coordinator = new AutomaticDiscoveryCoordinator(
            sources,
            NullLogger<AutomaticDiscoveryCoordinator>.Instance);

        var result = await coordinator.DiscoverAsync(CreateContext(), CancellationToken.None);

        Assert.Single(result.Candidates);
        Assert.Equal("https://example.com/safe", result.Candidates[0].Url);
        Assert.True(result.RejectedUnsafe + result.RejectedFiltered >= 1);
        Assert.True(
            result.UrlRejectionReasons.ContainsKey("url.ssrf_or_unsafe")
            || result.RejectedUnsafe >= 1);
    }

    private static UrlDiscoveryContext CreateContext()
    {
        return new UrlDiscoveryContext
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            MaxUrls = 50,
            MaxCandidateDomains = 50,
            MaxDiscoveryStages = 3,
            MinCandidatesBeforeRecovery = 10,
            Countries = ["Saudi Arabia"],
            Industries = ["Cleaning"]
        };
    }

    [Fact]
    public async Task DiscoverAsync_TriggersRecoveryWhenPrimaryProvidersReturnTooFewCandidates()
    {
        var sources = new IUrlDiscoverySource[]
        {
            new FakeDiscoverySource("DirectoryCatalog", enabled: true, []),
            new FakeDiscoverySource(
                "CommonCrawlIndex",
                enabled: true,
                [Candidate("https://example.com/archive-company")])
        };

        var coordinator = new AutomaticDiscoveryCoordinator(
            sources,
            NullLogger<AutomaticDiscoveryCoordinator>.Instance);

        var result = await coordinator.DiscoverAsync(CreateContext(), CancellationToken.None);

        Assert.Single(result.Candidates);
        Assert.Contains(result.Stages, x => x.RecoveryTriggered);
        Assert.Contains(result.ProviderStats, x => x.ProviderName == "CommonCrawlIndex" && x.Succeeded);
    }

    private static DiscoveredUrlCandidate Candidate(string url)
    {
        return new DiscoveredUrlCandidate
        {
            Url = url,
            SourceType = DiscoverySourceType.DirectoryCatalog,
            SourceName = "Test",
            Depth = 0
        };
    }

    private sealed class FakeDiscoverySource : IUrlDiscoverySource
    {
        private readonly bool _enabled;
        private readonly IReadOnlyList<DiscoveredUrlCandidate> _candidates;

        public FakeDiscoverySource(
            string name,
            bool enabled,
            IReadOnlyList<DiscoveredUrlCandidate> candidates)
        {
            Name = name;
            _enabled = enabled;
            _candidates = candidates;
        }

        public string Name { get; }

        public bool IsEnabled(UrlDiscoveryContext context) => _enabled;

        public Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
            UrlDiscoveryContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_candidates);
        }
    }

    private sealed class ThrowingDiscoverySource : IUrlDiscoverySource
    {
        public ThrowingDiscoverySource(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool IsEnabled(UrlDiscoveryContext context) => true;

        public Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
            UrlDiscoveryContext context,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated provider failure");
        }
    }
}
