using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Infrastructure.Persistence;
using Sendly.Infrastructure.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Sendly.Application.Tests;

public class ExperienceOpsDiscoveryRegressionTests
{
    private static readonly string[] ApprovedIndustries =
    [
        "Sailing Center", "Archery Club", "Climbing Gym", "Photography Studio",
        "Dive Center", "Canoe Rental", "Go-Kart Track", "Art Studio",
        "Bicycle Workshop", "Event Equipment Rental", "Escape Room",
        "Bicycle Rental", "Kayak Rental", "Pottery Studio"
    ];

    [Fact]
    public void Planner_covers_all_approved_industries_and_skips_exclusions()
    {
        var context = CreateExperienceOpsContext();
        var planned = SearchCampaignPlanner.Build(context, 50);

        foreach (var industry in ApprovedIndustries)
        {
            Assert.Contains(planned, x => x.Industry.Equals(industry, StringComparison.OrdinalIgnoreCase));
        }

        Assert.DoesNotContain(planned, x =>
            x.Query.Contains("school", StringComparison.OrdinalIgnoreCase)
            && !x.Query.Contains("sailing", StringComparison.OrdinalIgnoreCase)
            && !x.Query.Contains("dive", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(planned, x => x.Query.Contains("booking platform", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(planned, x => x.Language == "ar");
    }

    [Fact]
    public void Sailing_school_does_not_expand_into_education_school_family()
    {
        var expanded = IndustrySynonymCatalog.Expand(["Sailing Center", "sailing school"]);
        Assert.DoesNotContain(expanded, x => x.Equals("education", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(expanded, x => x.Equals("private school", StringComparison.OrdinalIgnoreCase));
        Assert.Null(IndustrySynonymCatalog.MatchIndustry("Deutsche Internationale Schule Sharjah", ["Sailing Center"]));
    }

    [Fact]
    public void Dead_domain_is_rejected_by_health_policy()
    {
        Assert.True(DomainHealthPolicy.IsParkingOrMarketplaceHost("https://0007.ae/beaches"));
        Assert.True(DomainHealthPolicy.ShouldRejectArchiveCandidate(
            "https://0007.ae/american-schools",
            ApprovedIndustries,
            new Dictionary<string, int> { ["0007.ae"] = 5 }));
        Assert.False(DomainHealthPolicy.ShouldRejectArchiveCandidate(
            "https://example-kayak.ae/kayak-rental",
            ApprovedIndustries,
            new Dictionary<string, int>()));
    }

    [Fact]
    public void Registry_chrome_urls_are_not_directory_listings()
    {
        var pricing = UrlAdmissionPolicy.Evaluate("https://opencorporates.com/pricing/");
        Assert.False(pricing.Accepted);
        Assert.Equal("url.registry_chrome", pricing.ReasonCode);

        var privacy = UrlAdmissionPolicy.Evaluate("https://opencorporates.com/user-privacy-policy/");
        Assert.False(privacy.Accepted);

        var parked = UrlAdmissionPolicy.Evaluate("https://0007.ae/");
        Assert.False(parked.Accepted);
    }

    [Fact]
    public void Navigation_and_legal_copy_are_not_organizations()
    {
        Assert.True(ProspectJunkClassifier.IsJunkOrganization("Advanced search", "https://opencorporates.com/advanced-search/"));
        Assert.True(ProspectJunkClassifier.IsJunkOrganization("Academia", "https://opencorporates.com/use-cases/academia"));
        Assert.True(ProspectJunkClassifier.IsJunkOrganization("IMPORTANT INFORMATION AND WHO WE ARE", "https://opencorporates.com/user-privacy-policy/"));
        Assert.True(ProspectJunkClassifier.IsJunkOrganization(
            "Make offer Your offer must be higher than the seller’s minimum price.",
            "https://0007.ae/"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Government"));
        Assert.False(ProspectJunkClassifier.IsJunkOrganization("Dubai Kayak Rental", "https://dubaikayak.ae/"));
    }

    [Fact]
    public void Phone_extractor_rejects_years_prices_and_ids()
    {
        Assert.False(HtmlPageExtractor.IsLikelyPhone("2024"));
        Assert.False(HtmlPageExtractor.IsLikelyPhone("20240115"));
        Assert.False(HtmlPageExtractor.IsLikelyPhone("AED 1500.00"));
        Assert.False(HtmlPageExtractor.IsLikelyPhone("ID 12345678"));
        Assert.True(HtmlPageExtractor.IsLikelyPhone("+971 4 123 4567"));
    }

    [Fact]
    public void Qualification_rejects_school_as_sailing_center()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Deutsche Internationale Schule Sharjah",
                Website = "https://dssharjah.org",
                Country = "United Arab Emirates",
                CountryEvidenceSource = "page_address",
                Industry = "Sailing Center",
                CompanyDescription = "International school in Sharjah offering K-12 education and admissions.",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 80,
                NameConfidence = 80,
                Email = "office@dssharjah.org",
                NameReasonCode = "identity.title_corroborated"
            },
            QueryVersion = new ResearchQueryVersion { TargetIndustry = "Sailing Center" },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["United Arab Emirates"],
            Industries = ApprovedIndustries.ToList(),
            ExcludedIndustries = ["School", "University", "Education"],
            ExcludedKeywords = ["school", "admission", "tuition"]
        });

        Assert.False(result.IsQualified);
        Assert.Contains(result.Criteria, x => x.CriterionKey == "exclusions" && !x.Passed
                                              || x.CriterionKey == "targetIndustry" && !x.Passed);
    }

    [Fact]
    public void Qualification_rejects_fitness_club_as_archery_club()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "City Fitness Club",
                Website = "https://cityfitness.ae",
                Country = "United Arab Emirates",
                CountryEvidenceSource = "page_address",
                Industry = "Archery Club",
                CompanyDescription = "Generic gym and fitness club with weights and cardio.",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 80,
                NameConfidence = 80,
                Email = "info@cityfitness.ae",
                NameReasonCode = "identity.title_corroborated"
            },
            QueryVersion = new ResearchQueryVersion { TargetIndustry = "Archery Club" },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["United Arab Emirates"],
            Industries = ["Archery Club"]
        });

        Assert.False(result.IsQualified);
        Assert.Contains(result.Criteria, x => x.CriterionKey == "targetIndustry" && !x.Passed);
    }

    [Fact]
    public void High_score_cannot_override_failed_exclusion_gate()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Sharjah Booking Platform",
                Website = "https://book.example",
                Country = "United Arab Emirates",
                CountryEvidenceSource = "page_address",
                Industry = "Escape Room",
                CompanyDescription = "Online booking platform for escape rooms and activities.",
                EntityType = ResearchEntityType.DirectoryAggregator,
                EntityTypeConfidence = 90,
                NameConfidence = 90,
                Email = "sales@book.example",
                NameReasonCode = "identity.title_corroborated"
            },
            MinQualificationScore = 10,
            Countries = ["United Arab Emirates"],
            Industries = ["Escape Room"],
            ExcludedIndustries = ["Booking Platform"]
        });

        Assert.False(result.IsQualified);
        Assert.True(result.Score < 10 || result.Criteria.Any(x => x.IsMandatory && !x.Passed));
    }

    [Fact]
    public async Task Sqlite_prior_research_orders_by_time_without_datetimeoffset_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sendly-dt-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<SendlyDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        try
        {
            await using var db = new SendlyDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var project = new OutreachProject { Name = "ExperienceOps – UAE" };
            db.OutreachProjects.Add(project);
            await db.SaveChangesAsync();

            db.Prospects.AddRange(
                new Prospect
                {
                    ProjectId = project.Id,
                    CompanyName = "Older Kayak Co",
                    Website = "https://old-kayak.ae",
                    Country = "United Arab Emirates",
                    Industry = "Kayak Rental",
                    Status = ProspectStatus.Discovered,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                    UpdatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2)
                },
                new Prospect
                {
                    ProjectId = project.Id,
                    CompanyName = "Newer Kayak Co",
                    Website = "https://new-kayak.ae",
                    Country = "United Arab Emirates",
                    Industry = "Kayak Rental",
                    Status = ProspectStatus.Discovered,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                },
                new Prospect
                {
                    ProjectId = project.Id,
                    CompanyName = "IMPORTANT INFORMATION AND WHO WE ARE",
                    SourceUrl = "https://opencorporates.com/user-privacy-policy/",
                    Status = ProspectStatus.Discovered,
                    UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
                });
            await db.SaveChangesAsync();

            var source = new PriorResearchUrlDiscoverySource(db);
            var discovered = await source.DiscoverAsync(
                new UrlDiscoveryContext
                {
                    ProjectId = project.Id,
                    EnablePriorResearchUrls = true,
                    MaxUrls = 20,
                    MaxPriorResearchSharePercent = 40,
                    Countries = ["United Arab Emirates"],
                    Industries = ["Kayak Rental"]
                },
                CancellationToken.None);

            Assert.Contains(discovered, x => x.Url.Contains("new-kayak.ae", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(discovered, x => x.Url.Contains("opencorporates", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Search_query_emits_event_for_zero_results()
    {
        var recorder = new RecordingEventRecorder();
        var provider = new Mock<IWebSearchProvider>();
        provider.SetupGet(x => x.Name).Returns("SearXNG");
        provider.SetupGet(x => x.IsEnabled).Returns(true);
        provider.Setup(x => x.SearchAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var source = new SearchProspectSource(
            [provider.Object],
            new WebsitePageCrawler(Mock.Of<IHttpClientFactory>(), NullLogger<WebsitePageCrawler>.Instance),
            Mock.Of<IAiResearchService>(),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions
            {
                EnableSearchProvider = true,
                MaxSearchQueries = 3,
                MaxSearchRequests = 10,
                MaxSearchResults = 5,
                MaxPagesPerCrawl = 1,
                MaxResearchDurationMinutes = 30
            }),
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions()),
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions { Enabled = true, BaseUrl = "http://127.0.0.1:8080" }),
            NullLogger<SearchProspectSource>.Instance,
            events: recorder);

        var context = CreateExperienceOpsContext();
        context.ResearchRunId = Guid.NewGuid();
        context.DiscoveryBatchSize = 5;
        context.DiscoveryState.PlannedSearchQueries = SearchCampaignPlanner.Build(context, 3).ToList();

        await source.DiscoverAsync(context, CancellationToken.None);

        Assert.Contains(recorder.Events, x => x.EventType == ResearchRunEventType.SearchQueryStarted);
        Assert.Contains(recorder.Events, x =>
            x.EventType is ResearchRunEventType.SearchQueryNoResults or ResearchRunEventType.SearchQueryCompleted);
        Assert.True(context.DiscoveryState.SearchQueriesExecuted >= 1);
    }

    [Fact]
    public async Task Unavailable_search_provider_does_not_consume_query_budget()
    {
        var recorder = new RecordingEventRecorder();
        var provider = new Mock<IDiagnosticWebSearchProvider>();
        provider.SetupGet(x => x.Name).Returns("SearXNG");
        provider.SetupGet(x => x.IsEnabled).Returns(true);
        provider.Setup(x => x.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebSearchOutcome.Unavailable("SearXNG", "engines suspended"));
        provider.Setup(x => x.SearchDetailedAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("should not search"));

        var source = new SearchProspectSource(
            [provider.Object],
            new WebsitePageCrawler(Mock.Of<IHttpClientFactory>(), NullLogger<WebsitePageCrawler>.Instance),
            Mock.Of<IAiResearchService>(),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions
            {
                EnableSearchProvider = true,
                MaxSearchQueries = 40,
                MaxSearchRequests = 80,
                MaxResearchDurationMinutes = 30
            }),
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions()),
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions { Enabled = true, BaseUrl = "http://127.0.0.1:8080" }),
            NullLogger<SearchProspectSource>.Instance,
            events: recorder);

        var context = CreateExperienceOpsContext();
        context.ResearchRunId = Guid.NewGuid();
        context.DiscoveryState.PlannedSearchQueries = SearchCampaignPlanner.Build(context, 20).ToList();

        var result = await source.DiscoverAsync(context, CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, context.DiscoveryState.SearchQueriesExecuted);
        Assert.Contains(recorder.Events, x => x.EventType == ResearchRunEventType.SearchProviderHealthChecked);
        Assert.Contains(recorder.Events, x => x.EventType == ResearchRunEventType.ResearchBlocked);
        provider.Verify(
            x => x.SearchDetailedAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Junk_preview_lists_navigation_prospects_without_deleting()
    {
        await using var db = CreateInMemoryDb();
        var project = new OutreachProject { Name = "ExperienceOps – UAE" };
        db.OutreachProjects.Add(project);
        var run = new ResearchRun { ProjectId = project.Id, Status = ResearchRunStatus.Completed };
        db.ResearchRuns.Add(run);
        db.Prospects.Add(new Prospect
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            CompanyName = "Advanced search",
            SourceUrl = "https://opencorporates.com/advanced-search/",
            Status = ProspectStatus.Discovered
        });
        db.Prospects.Add(new Prospect
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            CompanyName = "Dubai Kayak Rental",
            Website = "https://dubaikayak.ae",
            Status = ProspectStatus.Discovered
        });
        await db.SaveChangesAsync();

        var service = new ProspectJunkAuditService(db);
        var preview = await service.PreviewAsync(project.Id, run.Id, CancellationToken.None);
        Assert.Equal(1, preview.Count);
        Assert.Equal("Advanced search", preview.Items[0].CompanyName);

        var quarantined = await service.QuarantineAsync([preview.Items[0].Id], CancellationToken.None);
        Assert.Equal(1, quarantined);
        var junk = await db.Prospects.AsNoTracking().FirstAsync(x => x.CompanyName == "Advanced search");
        var legit = await db.Prospects.AsNoTracking().FirstAsync(x => x.CompanyName == "Dubai Kayak Rental");
        Assert.Equal(ProspectStatus.Rejected, junk.Status);
        Assert.Equal(ProspectStatus.Discovered, legit.Status);
    }

    private static ProspectSourceContext CreateExperienceOpsContext()
        => new()
        {
            ProjectName = "ExperienceOps – UAE",
            Countries = ["United Arab Emirates"],
            Industries = ApprovedIndustries.ToList(),
            ExcludedIndustries = ["School", "University", "Booking Platform"],
            ExcludedKeywords = ["school", "admission", "directory website"],
            Locations = ["Dubai", "Abu Dhabi", "Sharjah", "Ajman", "Ras Al Khaimah", "Fujairah"],
            TargetRoles = ["Owner", "Founder", "General Manager"],
            Keywords =
            [
                "escape room", "pottery studio", "kayak rental", "climbing gym", "dive center",
                "go-kart track", "bicycle rental", "archery club"
            ],
            Specification = new ResearchSpecification
            {
                ApprovedIndustries = ApprovedIndustries.ToList(),
                TargetCountries = ["United Arab Emirates"],
                Cities = ["Dubai", "Abu Dhabi", "Sharjah", "Ajman", "Ras Al Khaimah", "Fujairah"],
                LanguageHints = ["en", "ar"]
            },
            RequestedCount = 50
        };

    private static SendlyDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<SendlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SendlyDbContext(options);
    }

    private sealed class RecordingEventRecorder : IResearchEventRecorder
    {
        public List<ResearchEventRecordRequest> Events { get; } = [];

        public Task<ResearchRunEventDto> RecordAsync(
            ResearchEventRecordRequest request,
            CancellationToken cancellationToken)
        {
            Events.Add(request);
            return Task.FromResult(new ResearchRunEventDto
            {
                ResearchRunId = request.ResearchRunId,
                EventType = request.EventType.ToString(),
                Message = request.Message
            });
        }
    }
}
