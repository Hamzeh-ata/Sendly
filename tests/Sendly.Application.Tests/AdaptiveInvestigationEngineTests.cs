using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Application.Services.WebsiteInvestigation;
using Sendly.Application.Tests.Fakes;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Infrastructure.Persistence;
using Sendly.Infrastructure.Research;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Sendly.Application.Tests;

public class AdaptiveInvestigationEngineTests
{
    [Fact]
    public void ExperienceOps_specification_keeps_all_industries_and_exclusions_separate()
    {
        var spec = ProjectUnderstandingEngine.Interpret(CreateExperienceOpsRequest());

        Assert.True(spec.ApprovedIndustries.Count >= 10);
        Assert.Contains(spec.ApprovedIndustries, x => x.Contains("Escape Room", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(spec.ApprovedIndustries, x => x.Contains("Pottery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(spec.ApprovedIndustries, x => x.Contains("Kayak", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(spec.ApprovedIndustries, x => x.Equals("School", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(spec.ExcludedIndustries, x => x.Contains("School", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(spec.Cities, x => x.Equals("Dubai", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(spec.Cities, x => x.Equals("Abu Dhabi", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(spec.CorrectionReasons, x => x.Contains("full approved industry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QueryGeneration_covers_approved_industries_without_exclusion_terms()
    {
        var context = CreateExperienceOpsContext();
        var planned = ResearchSearchQueryGenerator.GeneratePlan(context, 80);
        var queries = planned.Select(x => x.Query).ToList();

        Assert.Contains(planned, x => x.Industry.Contains("Escape Room", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(planned, x => x.Industry.Contains("Archery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(planned, x => x.Location != null && x.Location.Contains("Dubai", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(planned, x => x.Intent == SearchQueryIntents.Directory);
        Assert.Contains(planned, x => x.Intent == SearchQueryIntents.ContactPage);
        Assert.DoesNotContain(queries, q => q.Contains("-school", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("private school", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("international school", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("مدرسة", StringComparison.Ordinal));
        Assert.DoesNotContain(queries, q => q.Contains("kindergarten", StringComparison.OrdinalIgnoreCase));
        Assert.True(planned.Select(x => x.Industry).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 8);
    }

    [Fact]
    public void DirectoryListing_leads_to_profiles_official_sites_and_second_directory()
    {
        var html = """
            <html><body>
              <a rel="next" href="/directory?page=2">Next</a>
              <article class="listing-card">
                <h2>Desert Escape Rooms</h2>
                <a href="/operators/desert-escape">Profile</a>
                <a href="https://desertescape.ae">Website</a>
              </article>
              <article class="listing-card">
                <h2>Wadi Kayak Rental</h2>
                <a href="/operators/wadi-kayak">Profile</a>
                <a href="https://wadikayak.ae">Website</a>
              </article>
              <a href="https://uae-activities-list.example/emirate/dubai">More Dubai operators</a>
            </body></html>
            """;
        var pageUrl = "https://uae-experiences.example/directory";
        var extracted = HtmlPageExtractor.Extract(html, new Uri(pageUrl));
        var result = WebsiteInvestigationEngine.InvestigateFetchedPage(new FetchedPageInvestigationRequest
        {
            PageUrl = pageUrl,
            NormalizedDomain = "uae-experiences.example",
            Html = html,
            Extracted = extracted,
            UrlPurpose = UrlPagePurpose.DirectoryListing,
            SourceType = DiscoverySourceType.DirectoryCatalog,
            IsDirectorySeed = true,
            Depth = 0,
            MaxDepth = 6,
            Industries = CreateExperienceOpsContext().Industries,
            Countries = ["United Arab Emirates"],
            DiscoveryState = new ResearchDiscoveryState()
        });

        Assert.Contains(result.OrganizationCandidates, x => x.CompanyName.Contains("Desert Escape", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.FollowUps, x => x.Url.Contains("desertescape.ae", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.FollowUps, x => x.Url.Contains("/operators/desert-escape", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.FollowUps, x => x.ExpectedPageKind == InvestigationPageKind.Pagination || x.Url.Contains("page=2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(extracted.ExternalLinks, x => x.Contains("uae-activities-list.example", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IndependentCrawl_discovers_nested_contacts_on_official_site()
    {
        await using var db = CreateDb();
        var project = await SeedExperienceOpsProjectAsync(db);
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        fetcher.Map("https://example.com/directory", FakeSafeHttpFetcher.Html(
            "https://example.com/directory",
            """
            <html><body>
              <article class="listing-card"><h2>Peak Climbing Gym</h2>
                <a href="https://example.com/peak-climbing">Website</a></article>
            </body></html>
            """));
        fetcher.Map("https://example.com/peak-climbing", FakeSafeHttpFetcher.Html(
            "https://example.com/peak-climbing",
            """
            <html><head><title>Peak Climbing Gym Dubai</title></head>
            <body>Indoor climbing and bouldering in Dubai.
            <a href="/peak-climbing/contact">Contact</a><a href="/peak-climbing/about">About</a></body></html>
            """));
        fetcher.Map("https://example.com/peak-climbing/contact", FakeSafeHttpFetcher.Html(
            "https://example.com/peak-climbing/contact",
            """
            <html><head><title>Contact Peak Climbing Gym</title></head>
            <body>Email <a href="mailto:hello@example.com">hello@example.com</a>
            and bookings <a href="mailto:bookings@example.com">bookings@example.com</a>
            Phone <a href="tel:+97145001111">+971 4 500 1111</a></body></html>
            """));
        fetcher.Map("https://example.com/peak-climbing/about", FakeSafeHttpFetcher.Html(
            "https://example.com/peak-climbing/about",
            """
            <html><head><title>About Peak Climbing Gym</title></head>
            <body>Climbing gym and bouldering in Dubai, United Arab Emirates.
            Press: <a href="mailto:press@example.com">press@example.com</a></body></html>
            """));

        db.DiscoveredUrls.AddRange(
            new DiscoveredUrl
            {
                ResearchRunId = run.Id,
                ProjectId = project.Id,
                Url = "https://example.com/directory",
                NormalizedUrl = "https://example.com/directory",
                NormalizedDomain = "example.com",
                SourceType = DiscoverySourceType.DirectoryCatalog,
                SourceName = "UAE directory",
                Status = DiscoveredUrlStatus.Pending,
                PagePurpose = UrlPagePurpose.DirectoryListing.ToString(),
                Depth = 0,
                PriorityScore = 90
            },
            new DiscoveredUrl
            {
                ResearchRunId = run.Id,
                ProjectId = project.Id,
                Url = "https://example.com/peak-climbing",
                NormalizedUrl = "https://example.com/peak-climbing",
                NormalizedDomain = "example.com",
                SourceType = DiscoverySourceType.SeedUrl,
                SourceName = "Official website",
                Status = DiscoveredUrlStatus.Pending,
                PagePurpose = UrlPagePurpose.CompanyProfile.ToString(),
                Depth = 0,
                PriorityScore = 85
            },
            new DiscoveredUrl
            {
                ResearchRunId = run.Id,
                ProjectId = project.Id,
                Url = "https://example.com/peak-climbing/contact",
                NormalizedUrl = "https://example.com/peak-climbing/contact",
                NormalizedDomain = "example.com",
                SourceType = DiscoverySourceType.SeedUrl,
                SourceName = "Contact page",
                Status = DiscoveredUrlStatus.Pending,
                PagePurpose = UrlPagePurpose.Contact.ToString(),
                Depth = 1,
                PriorityScore = 95
            },
            new DiscoveredUrl
            {
                ResearchRunId = run.Id,
                ProjectId = project.Id,
                Url = "https://example.com/peak-climbing/about",
                NormalizedUrl = "https://example.com/peak-climbing/about",
                NormalizedDomain = "example.com",
                SourceType = DiscoverySourceType.SeedUrl,
                SourceName = "About page",
                Status = DiscoveredUrlStatus.Pending,
                PagePurpose = UrlPagePurpose.About.ToString(),
                Depth = 1,
                PriorityScore = 80
            });
        await db.SaveChangesAsync();

        var crawler = CreateCrawler(db, fetcher, [
            new FixedDiscoverySource("DirectoryCatalog", [])
        ]);
        var state = new ResearchDiscoveryState
        {
            StartedAtUtc = DateTimeOffset.UtcNow,
            IndependentDiscoverySeeded = true
        };
        var context = CreateExperienceOpsContext();
        context.ProjectId = project.Id;
        context.ResearchRunId = run.Id;
        context.DiscoveryState = state;
        context.DiscoveryBatchSize = 12;

        var first = await crawler.DiscoverAsync(context, CancellationToken.None);
        var second = await crawler.DiscoverAsync(context, CancellationToken.None);
        var third = await crawler.DiscoverAsync(context, CancellationToken.None);
        var all = first.Concat(second).Concat(third).ToList();

        Assert.Contains(all, x => x.CompanyName.Contains("Peak Climbing", StringComparison.OrdinalIgnoreCase));
        var gyms = all.Where(x => x.CompanyName.Contains("Peak Climbing", StringComparison.OrdinalIgnoreCase)).ToList();
        var emails = gyms
            .SelectMany(x => x.AttributedEmails.Select(e => e.Value).Concat(x.AdditionalEmails).Append(x.Email))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        Assert.Contains("hello@example.com", emails, StringComparer.OrdinalIgnoreCase);
        Assert.True(emails.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2);
        Assert.Contains(gyms, x => string.Equals(x.Industry, "Climbing Gym", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Qualification_rejects_schools_government_directories_and_articles()
    {
        var service = new ProspectQualificationService();
        var request = CreateExperienceOpsQualificationRequest("Deutsche Internationale Schule Sharjah", "https://www.dssharjah.org", "German international school in Sharjah");
        var school = service.Evaluate(request);
        Assert.False(school.IsQualified);
        Assert.Contains(school.Criteria, x => x.CriterionKey == "exclusionCriteria" && !x.Passed);

        var authority = service.Evaluate(CreateExperienceOpsQualificationRequest(
            "هيئة الشارقة للتعليم الخاص",
            "https://spea.shj.ae",
            "Sharjah Private Education Authority"));
        Assert.False(authority.IsQualified);

        var directory = service.Evaluate(CreateExperienceOpsQualificationRequest(
            "UAE Business Directory",
            "https://directory.example/listings",
            "Find a business directory of companies"));
        Assert.False(directory.IsQualified);

        var article = service.Evaluate(CreateExperienceOpsQualificationRequest(
            "١٠ من أفضل المدارس البريطانية في عجمان",
            "https://gjproperties.ae/best-schools",
            "List of the best British schools in Ajman"));
        Assert.False(article.IsQualified);
    }

    [Fact]
    public void Exclusion_recognizes_arabic_and_german_school_evidence()
    {
        var arabic = ExclusionEvidenceClassifier.Classify(
            "مدرسة منارة الشارقة الخاصة",
            "مدرسة خاصة",
            "تعليم أساسي في الشارقة",
            "https://www.manaratalsharjah.com",
            ["School", "Education"],
            ["school"]);
        Assert.True(arabic.IsExcluded);

        var german = ExclusionEvidenceClassifier.Classify(
            "Grundschule am Park",
            "Willkommen an unserer Schule",
            "Unsere Schüler lernen in Berlin",
            "https://grundschule-park.de",
            ["School"],
            ["schule"]);
        Assert.True(german.IsExcluded);
    }

    [Fact]
    public void Foreign_school_cannot_qualify_for_uae_activity_project()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(CreateExperienceOpsQualificationRequest(
            "Lincoln High School",
            "https://lincolnhs.edu",
            "Public high school in Ohio",
            country: "United States"));
        Assert.False(result.IsQualified);
        Assert.Contains(result.Criteria, x => !x.Passed && (x.CriterionKey == "targetCountry" || x.CriterionKey == "exclusionCriteria" || x.CriterionKey == "targetIndustry"));
    }

    [Fact]
    public void Industry_is_classified_independently_from_query_primary_industry()
    {
        var result = IndustryEvidenceClassifier.Classify(
            "Peak Climbing Gym",
            "Peak Climbing Gym Dubai",
            "Indoor climbing and bouldering sessions in Dubai",
            null,
            ["Sailing Center", "Climbing Gym", "Escape Room"],
            ["School"]);
        Assert.True(result.MatchedApprovedIndustry);
        Assert.Equal("Climbing Gym", result.AssignedIndustry);
        Assert.False(result.IsExcluded);
    }

    [Fact]
    public void Dates_css_favicons_and_error_pages_are_not_contacts_or_organizations()
    {
        var datePage = HtmlPageExtractor.Extract(
            "<html><head><title>School calendar</title></head><body>Academic year 2024-2025 and 07-08-2025</body></html>",
            new Uri("https://school.example/calendar"));
        Assert.DoesNotContain(datePage.Phones, HtmlPageExtractor.IsLikelyPhone);

        var css = HtmlPageExtractor.Extract("body { color: red; content: 'info@example.com'; }", new Uri("https://example.com/assets/app.css"));
        Assert.True(css.IsNonHtmlAsset);
        Assert.Empty(css.Emails);

        var favicon = HtmlPageExtractor.Extract("binary", new Uri("https://example.com/favicon.ico"));
        Assert.True(favicon.IsNonHtmlAsset);

        var error = HtmlPageExtractor.Extract(
            "<html><head><title>404 Not Found</title></head><body>Email admin@example.com</body></html>",
            new Uri("https://example.com/missing"));
        Assert.True(error.IsErrorPage);
        Assert.Empty(error.Emails);
        Assert.Null(error.OrganizationName);
    }

    [Fact]
    public async Task Sqlite_prior_research_orders_without_datetimeoffset_exception()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SendlyDbContext>().UseSqlite(connection).Options;
        await using var db = new SendlyDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var project = new OutreachProject { Name = "ExperienceOps – UAE", IsActive = true };
        db.OutreachProjects.Add(project);
        await db.SaveChangesAsync();

        db.Prospects.AddRange(
            CreateProspect(project.Id, "Old Climbing", "https://old-climb.ae", DateTimeOffset.Parse("2026-01-01T00:00:00+04:00")),
            CreateProspect(project.Id, "New Climbing", "https://new-climb.ae", DateTimeOffset.Parse("2026-10-01T00:00:00-07:00")),
            CreateProspect(project.Id, "Mid Climbing", "https://mid-climb.ae", DateTimeOffset.Parse("2026-06-01T00:00:00+00:00")));
        await db.SaveChangesAsync();

        var source = new PriorResearchUrlDiscoverySource(db);
        var urls = await source.DiscoverAsync(
            new UrlDiscoveryContext
            {
                ProjectId = project.Id,
                Countries = ["United Arab Emirates"],
                Industries = ["Climbing Gym"],
                EnablePriorResearchUrls = true,
                MaxUrls = 20,
                MaxPriorResearchSharePercent = 40
            },
            CancellationToken.None);

        Assert.NotEmpty(urls);
        Assert.Contains(urls, x => x.Url.Contains("new-climb.ae", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("new-climb.ae", UrlHost(urls[0].Url));
    }

    [Fact]
    public async Task Sqlite_prior_research_handles_empty_history_and_duplicates()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SendlyDbContext>().UseSqlite(connection).Options;
        await using var db = new SendlyDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var source = new PriorResearchUrlDiscoverySource(db);
        var empty = await source.DiscoverAsync(
            new UrlDiscoveryContext
            {
                ProjectId = Guid.NewGuid(),
                EnablePriorResearchUrls = true,
                MaxUrls = 10,
                MaxPriorResearchSharePercent = 40
            },
            CancellationToken.None);
        Assert.Empty(empty);

        var project = new OutreachProject { Name = "Ops", IsActive = true };
        db.OutreachProjects.Add(project);
        await db.SaveChangesAsync();
        db.Prospects.Add(CreateProspect(project.Id, "Same Gym", "https://same.ae", DateTimeOffset.UtcNow));
        db.Prospects.Add(CreateProspect(project.Id, "Same Gym 2", "https://same.ae/about", DateTimeOffset.UtcNow.AddMinutes(-1)));
        await db.SaveChangesAsync();

        var urls = await source.DiscoverAsync(
            new UrlDiscoveryContext
            {
                ProjectId = project.Id,
                Countries = ["United Arab Emirates"],
                Industries = ["Climbing Gym"],
                EnablePriorResearchUrls = true,
                MaxUrls = 20,
                MaxPriorResearchSharePercent = 80
            },
            CancellationToken.None);
        Assert.Single(urls.Select(x => UrlHost(x.Url)).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Low_yield_search_does_not_starve_other_industries()
    {
        var planned = new List<PlannedSearchQuery>
        {
            new() { Query = "sailing center Dubai", Industry = "Sailing Center", Intent = SearchQueryIntents.DiscoverBusiness, Location = "Dubai" },
            new() { Query = "sailing center directory", Industry = "Sailing Center", Intent = SearchQueryIntents.Directory, Location = "Dubai" },
            new() { Query = "escape room Dubai", Industry = "Escape Room", Intent = SearchQueryIntents.DiscoverBusiness, Location = "Dubai" }
        };
        var state = new ResearchDiscoveryState();
        state.ExecutedSearchQueryKeys.Add(SearchCampaignPlanner.BuildKey(planned[0]));
        state.QueriesExecutedByIndustry["Sailing Center"] = 4;
        state.LowYieldQueryKeys.Add(SearchCampaignPlanner.BuildYieldKey(planned[0]));
        state.LowYieldQueryKeys.Add(SearchCampaignPlanner.BuildYieldKey(planned[1]));

        var next = SearchCampaignPlanner.SelectNext(planned, state);
        Assert.NotNull(next);
        Assert.Equal("Escape Room", next!.Industry);
    }

    [Fact]
    public async Task Live_events_persist_project_interpretation_and_query_plan()
    {
        await using var db = CreateDb();
        var notifier = new Mock<IResearchProgressNotifier>();
        notifier.Setup(x => x.NotifyEventAsync(It.IsAny<ResearchRunEventDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var recorder = new ResearchEventRecorder(db, notifier.Object);
        var run = new ResearchRun { ProjectId = Guid.NewGuid(), Status = ResearchRunStatus.Running };
        db.ResearchRuns.Add(run);
        await db.SaveChangesAsync();

        await recorder.RecordAsync(new ResearchEventRecordRequest
        {
            ResearchRunId = run.Id,
            EventType = ResearchRunEventType.ProjectInterpreted,
            Stage = ResearchPipelineStage.ProjectInterpretation,
            Message = "Interpreted ExperienceOps"
        }, CancellationToken.None);
        await recorder.RecordAsync(new ResearchEventRecordRequest
        {
            ResearchRunId = run.Id,
            EventType = ResearchRunEventType.SearchQueryGenerated,
            Stage = ResearchPipelineStage.SearchPlanning,
            Message = "Planned queries"
        }, CancellationToken.None);

        var events = await db.ResearchRunEvents.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        Assert.Contains(events, x => x.EventType == ResearchRunEventType.ProjectInterpreted);
        Assert.Contains(events, x => x.EventType == ResearchRunEventType.SearchQueryGenerated);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Llm_negative_assessment_is_not_overridden_by_rule_score()
    {
        var service = new ProspectQualificationService();
        var rules = service.Evaluate(CreateExperienceOpsQualificationRequest(
            "Deutsche Internationale Schule Sharjah",
            "https://www.dssharjah.org",
            "International school with office@dssharjah.org",
            email: "office@dssharjah.org"));
        Assert.False(rules.IsQualified);
        Assert.Contains(rules.Criteria, x => x.IsMandatory && !x.Passed);
    }

    [Fact]
    public void Host_fairness_does_not_give_one_domain_the_entire_batch()
    {
        var pending = new List<DiscoveredUrl>();
        for (var i = 0; i < 8; i++)
        {
            pending.Add(new DiscoveredUrl
            {
                Url = $"https://opencorporates.com/page-{i}",
                NormalizedUrl = $"https://opencorporates.com/page-{i}",
                NormalizedDomain = "opencorporates.com",
                Status = DiscoveredUrlStatus.Pending,
                SourceType = DiscoverySourceType.LinkExpansion,
                PriorityScore = 80
            });
        }

        pending.Add(new DiscoveredUrl
        {
            Url = "https://desertescape.ae/contact",
            NormalizedUrl = "https://desertescape.ae/contact",
            NormalizedDomain = "desertescape.ae",
            Status = DiscoveredUrlStatus.Pending,
            SourceType = DiscoverySourceType.LinkExpansion,
            PagePurpose = "Contact",
            PriorityScore = 40
        });

        var ranked = CrawlQueueScheduler.RankPending(
            pending,
            ["Escape Room"],
            ["United Arab Emirates"],
            new Dictionary<string, int>(),
            new HashSet<string>(),
            maxPagesPerDomain: 5,
            maxPerDomainInBatch: 2);

        Assert.Contains(ranked.Take(4), x => x.Domain.Equals("desertescape.ae", StringComparison.OrdinalIgnoreCase));
    }

    private static QualificationEvaluationRequest CreateExperienceOpsQualificationRequest(
        string name,
        string website,
        string description,
        string? country = "United Arab Emirates",
        string? email = null)
    {
        var context = CreateExperienceOpsContext();
        return new QualificationEvaluationRequest
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            Candidate = new ProspectCandidate
            {
                CompanyName = name,
                Website = website,
                SourceUrl = website,
                Country = country,
                CountryEvidenceSource = "page_address",
                CountryEvidenceSummary = country,
                CompanyDescription = description,
                Email = email,
                NameConfidence = 80,
                NameReasonCode = "identity.title_corroborated"
            },
            QueryVersion = new ResearchQueryVersion
            {
                TargetIndustry = "Sailing Center",
                RequireOfficialWebsite = true,
                RequirePublicBusinessEmail = true,
                TargetCountry = "United Arab Emirates"
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = context.Countries,
            Industries = context.Industries,
            ExcludedIndustries = context.ExcludedIndustries,
            ExcludedKeywords = context.ExcludedKeywords,
            Keywords = context.Keywords
        };
    }

    private static ProspectSourceContext CreateExperienceOpsContext()
    {
        var spec = ProjectUnderstandingEngine.Interpret(CreateExperienceOpsRequest());
        return new ProspectSourceContext
        {
            ProjectName = "ExperienceOps – UAE",
            Description = "Booking management for activities and rentals.",
            ResearchInstructions = CreateExperienceOpsRequest().ResearchInstructions,
            Keywords =
            [
                "escape room", "archery club", "pottery studio", "kayak rental", "climbing gym",
                "photography studio", "sailing center", "go-kart track", "event equipment rental", "bicycle rental"
            ],
            ExcludedKeywords = ["school", "university", "directory website", "booking platform"],
            Countries = ["United Arab Emirates"],
            Industries = spec.ApprovedIndustries,
            ExcludedIndustries = spec.ExcludedIndustries,
            Locations = spec.Cities,
            TargetRoles = ["Owner", "General Manager"],
            Specification = spec,
            RequestedCount = 50
        };
    }

    private static ProjectUnderstandingRequest CreateExperienceOpsRequest()
        => new()
        {
            ProjectName = "ExperienceOps – UAE",
            Description = "Find real operating SMBs in the UAE for ExperienceOps.",
            ResearchInstructions = """
                OBJECTIVE
                Find real, operating SMB experience businesses in the United Arab Emirates.

                APPROVED INDUSTRIES ONLY
                1. Escape Room
                2. Archery Club
                3. Pottery Studio
                4. Kayak Rental
                5. Climbing Gym
                6. Photography Studio
                7. Sailing Center
                8. Go-Kart Track
                9. Event Equipment Rental
                10. Bicycle Rental

                REJECT / EXCLUDE
                K-12 schools, universities, colleges, school districts, education directories
                Hotels, restaurants, travel agencies, booking platforms, directory sites

                SEARCH STRATEGY
                Search by industry and emirate/city. Dubai, Abu Dhabi, Sharjah, Ajman, Ras Al Khaimah, Fujairah, Al Ain.
                """,
            ApprovedIndustries =
            [
                "Escape Room", "Archery Club", "Pottery Studio", "Kayak Rental", "Climbing Gym",
                "Photography Studio", "Sailing Center", "Go-Kart Track", "Event Equipment Rental", "Bicycle Rental"
            ],
            ExcludedIndustries = ["School", "University", "Hotel", "Restaurant", "Travel Agency", "Booking Platform", "Business Directory"],
            ExcludedKeywords = ["school", "university", "directory website"],
            Countries = ["United Arab Emirates"],
            QueryTargetIndustry = "Sailing Center",
            QueryBusinessCategories = [],
            MinQualificationScore = 60,
            TargetProspectCount = 50,
            IncludeEmail = true
        };

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
            MaxPagesPerCrawl = 8,
            MaxDiscoveryCandidates = 50,
            MaxPriorResearchSharePercent = 40,
            MaxHostFetchFailuresBeforeAvoid = 2,
            MaxResearchDurationMinutes = 30
        });
        var coordinator = new AutomaticDiscoveryCoordinator(sources, NullLogger<AutomaticDiscoveryCoordinator>.Instance);
        var notifier = new Mock<IResearchProgressNotifier>();
        notifier.Setup(x => x.NotifyEventAsync(It.IsAny<ResearchRunEventDto>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        notifier.Setup(x => x.NotifyProgressAsync(It.IsAny<ResearchRunDetailDto>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new IndependentCrawlProspectSource(
            db,
            coordinator,
            fetcher,
            new FakeRobotsTxtService(),
            new SitemapParser(),
            new DiscoverySourceExpansionService(
                new DiscoverySourceRegistryService(db),
                fetcher,
                MsOptions.Create(new ResearchOptions { EnableDiscoverySourceExpansion = false })),
            new DiscoverySourceRegistryService(db),
            sources,
            new ResearchEventRecorder(db, notifier.Object),
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

    private static async Task<OutreachProject> SeedExperienceOpsProjectAsync(SendlyDbContext db)
    {
        var project = new OutreachProject
        {
            Name = "ExperienceOps – UAE",
            Description = "Activity operators",
            ResearchInstructions = "Find climbing gyms and escape rooms"
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
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        db.ResearchRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private static Prospect CreateProspect(Guid projectId, string name, string website, DateTimeOffset updated)
        => new()
        {
            ProjectId = projectId,
            CompanyName = name,
            Website = website,
            SourceUrl = website,
            Industry = "Climbing Gym",
            Country = "United Arab Emirates",
            Status = ProspectStatus.Discovered,
            CreatedAtUtc = updated,
            UpdatedAtUtc = updated
        };

    private static string UrlHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

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
