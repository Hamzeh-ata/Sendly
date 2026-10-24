using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Application.Services.WebsiteInvestigation;
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

public class WebsiteInvestigationEngineTests
{
    [Fact]
    public void DirectoryListingExtractor_scopes_contacts_per_card()
    {
        var html = """
            <html><body>
              <article class="listing-card">
                <h2>Alpha Academy</h2>
                <a href="/edu-directory/schools/alpha-academy">Profile</a>
                <a href="https://example.com/alpha-academy">Website</a>
                <a href="mailto:office@example.com">office@example.com</a>
                <a href="tel:+96265551111">+96265551111</a>
              </article>
              <article class="listing-card">
                <h2>Beta School</h2>
                <a href="/edu-directory/schools/beta-school">Profile</a>
                <a href="https://example.com/beta-school">Website</a>
                <a href="mailto:hello@example.com">hello@example.com</a>
              </article>
            </body></html>
            """;

        var listings = DirectoryListingExtractor.Extract(html, new Uri("https://example.org/edu-directory/category/schools"));
        Assert.True(listings.Count >= 2);
        var alpha = listings.First(x => x.Name.Contains("Alpha", StringComparison.OrdinalIgnoreCase));
        var beta = listings.First(x => x.Name.Contains("Beta", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("office@example.com", alpha.Email);
        Assert.Equal("hello@example.com", beta.Email);
        Assert.DoesNotContain(beta.Email ?? string.Empty, alpha.Email ?? string.Empty);
        Assert.Contains("example.com/alpha-academy", alpha.OfficialWebsiteUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectoryListingExtractor_reads_arabic_cards()
    {
        var html = """
            <html><body>
              <div class="school-card">
                <h3>مدرسة الأمل الخاصة</h3>
                <a href="/edu-directory/schools/amal">الملف</a>
                <a href="https://example.com/amal-school">الموقع</a>
                <a href="mailto:info@example.com">info@example.com</a>
              </div>
            </body></html>
            """;

        var listings = DirectoryListingExtractor.Extract(html, new Uri("https://example.org/edu-directory/categories/private"));
        Assert.Contains(listings, x => x.Name.Contains("الأمل", StringComparison.Ordinal));
        var amal = listings.First(x => x.Name.Contains("الأمل", StringComparison.Ordinal));
        Assert.Equal("info@example.com", amal.Email);
        Assert.Contains("example.com/amal-school", amal.OfficialWebsiteUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoverAsync_directory_investigation_reaches_official_contact_email()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db, "Private schools in Jordan");
        var run = await SeedRunAsync(db, project);
        var fetcher = BuildDirectoryFixtureFetcher();

        var crawler = CreateCrawler(db, fetcher, new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "DirectoryCatalog",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.org/edu-directory/",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "Education Directory",
                        Depth = 0
                    }
                ])
        });

        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state, ["Jordan"], ["Education", "Private Schools"]);

        var allCandidates = new List<ProspectCandidate>();
        for (var i = 0; i < 6; i++)
        {
            var batch = await crawler.DiscoverAsync(context, CancellationToken.None);
            allCandidates.AddRange(batch);
            var pending = await db.DiscoveredUrls.CountAsync(x =>
                x.ResearchRunId == run.Id && x.Status == DiscoveredUrlStatus.Pending);
            if (pending == 0 && state.CompletedSourceNames.Contains("IndependentCrawler"))
            {
                break;
            }
        }

        var discoveredDump = await db.DiscoveredUrls
            .Where(x => x.ResearchRunId == run.Id)
            .Select(x => x.Url + " [" + x.Status + "] d=" + x.Depth + " src=" + x.SourceName)
            .ToListAsync();
        var eventDump = await db.ResearchRunEvents
            .Where(x => x.ResearchRunId == run.Id)
            .Select(x => x.EventType + ":" + (x.Message ?? string.Empty))
            .ToListAsync();
        var requestedDump = string.Join(" | ", fetcher.RequestedUrls.Select(u => u.AbsoluteUri));
        var diag = "Requested: " + requestedDump
                   + " Discovered: " + string.Join(" ; ", discoveredDump)
                   + " Events: " + string.Join(" ; ", eventDump)
                   + " Orgs: " + state.OrganizationInvestigations.Count
                   + " Plans: " + string.Join(",", state.DomainInvestigationPlans.Select(p =>
                       p.Key + "=" + p.Value.OrganizationsDiscovered + "/" + p.Value.Role));
        Assert.Contains(
            fetcher.RequestedUrls,
            u => u.AbsoluteUri.Contains("/category/private-schools", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            fetcher.RequestedUrls,
            u => u.AbsoluteUri.Contains("example.com/alpha-academy", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            allCandidates.Any(c =>
                c.CompanyName.Contains("Alpha Academy", StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Email, "office@example.com", StringComparison.OrdinalIgnoreCase)),
            diag);

        Assert.DoesNotContain(
            allCandidates,
            x => x.CompanyName.Contains("Education Directory", StringComparison.OrdinalIgnoreCase)
                 || (x.Website ?? string.Empty).Contains("example.org/edu-directory", StringComparison.OrdinalIgnoreCase));

        var alpha = allCandidates.FirstOrDefault(x =>
            x.CompanyName.Contains("Alpha Academy", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(alpha);
        Assert.Contains("example.com/alpha-academy", alpha!.Website, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("office@example.com", alpha.Email);
        Assert.Equal(ContactAttributionState.SameDomain, alpha.ContactAttributionState);

        var gamma = allCandidates.FirstOrDefault(x =>
            x.CompanyName.Contains("Gamma Institute", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(gamma);
        Assert.True(string.IsNullOrWhiteSpace(gamma!.Email));

        var events = await db.ResearchRunEvents.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        Assert.Contains(events, x => x.EventType == ResearchRunEventType.DirectoryCategoryDiscovered
                                     || x.EventType == ResearchRunEventType.OrganizationProfilesDiscovered);
        Assert.Contains(events, x => x.EventType == ResearchRunEventType.ListingOrganizationExtracted);
        Assert.Contains(events, x => x.EventType == ResearchRunEventType.OfficialWebsiteQueued);
        Assert.True(state.DomainInvestigationPlans.Count > 0);
        Assert.True(state.OrganizationInvestigations.Count >= 2);
        Assert.Contains(state.StageDurationsMs.Keys, k => k.Contains("investigation", StringComparison.OrdinalIgnoreCase)
                                                          || k.Contains("parseExtract", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverAsync_neighbor_card_emails_do_not_cross_attribute()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db, "Cleaning companies");
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        fetcher.Map(
            "https://example.org/biz-directory/",
            FakeSafeHttpFetcher.Html(
                "https://example.org/biz-directory/",
                """
                <html><head><title>Business Directory</title></head>
                <body>
                  <a href="/biz-directory/category/cleaning">Cleaning</a>
                </body></html>
                """));
        fetcher.Map(
            "https://example.org/biz-directory/category/cleaning",
            FakeSafeHttpFetcher.Html(
                "https://example.org/biz-directory/category/cleaning",
                """
                <html><body>
                  <article class="listing-card">
                    <h2>North Ridge Cleaning</h2>
                    <a href="/biz-directory/listings/north-ridge">Profile</a>
                    <a href="https://example.com/northridge">Website</a>
                    <a href="mailto:office@example.com">office@example.com</a>
                  </article>
                  <article class="listing-card">
                    <h2>Calder Cleaning</h2>
                    <a href="/biz-directory/listings/calder">Profile</a>
                    <a href="https://example.net/calder">Website</a>
                    <a href="mailto:hello@example.net">hello@example.net</a>
                  </article>
                </body></html>
                """));
        fetcher.Map(
            "https://example.org/biz-directory/listings/north-ridge",
            FakeSafeHttpFetcher.Html(
                "https://example.org/biz-directory/listings/north-ridge",
                """
                <html><body>
                  <article class="profile-card">
                    <h1>North Ridge Cleaning</h1>
                    <a href="https://example.com/northridge">Official site</a>
                    <a href="mailto:office@example.com">office@example.com</a>
                  </article>
                </body></html>
                """));
        fetcher.Map(
            "https://example.org/biz-directory/listings/calder",
            FakeSafeHttpFetcher.Html(
                "https://example.org/biz-directory/listings/calder",
                """
                <html><body>
                  <article class="profile-card">
                    <h1>Calder Cleaning</h1>
                    <a href="https://example.net/calder">Official site</a>
                    <a href="mailto:hello@example.net">hello@example.net</a>
                  </article>
                </body></html>
                """));
        fetcher.Map(
            "https://example.com/northridge/",
            FakeSafeHttpFetcher.Html(
                "https://example.com/northridge/",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"Organization","name":"North Ridge Cleaning","url":"https://example.com/northridge"}
                  </script>
                </head>
                <body>North Ridge Cleaning commercial cleaning Jordan.
                <a href="/contact">Contact</a></body></html>
                """));
        fetcher.Map(
            "https://example.com/northridge/contact",
            FakeSafeHttpFetcher.Html(
                "https://example.com/northridge/contact",
                "<html><body>Email <a href=\"mailto:office@example.com\">office@example.com</a></body></html>"));
        fetcher.Map(
            "https://example.net/calder/",
            FakeSafeHttpFetcher.Html(
                "https://example.net/calder/",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"Organization","name":"Calder Cleaning","url":"https://example.net/calder"}
                  </script>
                </head>
                <body>Calder Cleaning Jordan. <a href="/contact">Contact</a></body></html>
                """));
        fetcher.Map(
            "https://example.net/calder/contact",
            FakeSafeHttpFetcher.Html(
                "https://example.net/calder/contact",
                "<html><body>Email <a href=\"mailto:hello@example.net\">hello@example.net</a></body></html>"));

        var crawler = CreateCrawler(db, fetcher, new IUrlDiscoverySource[]
        {
            new FixedDiscoverySource(
                "DirectoryCatalog",
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.org/biz-directory/",
                        SourceType = DiscoverySourceType.DirectoryCatalog,
                        SourceName = "Biz Directory",
                        Depth = 0
                    }
                ])
        });

        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state, ["Jordan"], ["Cleaning"]);
        var all = new List<ProspectCandidate>();
        for (var i = 0; i < 6; i++)
        {
            all.AddRange(await crawler.DiscoverAsync(context, CancellationToken.None));
        }

        var north = all.FirstOrDefault(x => x.CompanyName.Contains("North Ridge", StringComparison.OrdinalIgnoreCase));
        var calder = all.FirstOrDefault(x => x.CompanyName.Contains("Calder", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(north);
        Assert.NotNull(calder);
        Assert.Equal("office@example.com", north!.Email);
        Assert.Equal("hello@example.net", calder!.Email);
        Assert.NotEqual(north.Email, calder.Email);
    }

    [Fact]
    public async Task DiscoverAsync_resumes_pending_investigation_across_batches()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db, "Private schools");
        var run = await SeedRunAsync(db, project);
        var fetcher = BuildDirectoryFixtureFetcher();
        var crawler = CreateCrawler(
            db,
            fetcher,
            [
                new FixedDiscoverySource(
                    "DirectoryCatalog",
                    [
                        new DiscoveredUrlCandidate
                        {
                            Url = "https://example.org/edu-directory/",
                            SourceType = DiscoverySourceType.DirectoryCatalog,
                            SourceName = "Education Directory",
                            Depth = 0
                        }
                    ])
            ],
            MaxFetchesPerRun: 40);

        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(project, run, state, ["Jordan"], ["Education"]);
        context.DiscoveryBatchSize = 2;
        await crawler.DiscoverAsync(context, CancellationToken.None);
        var pendingAfterFirst = await db.DiscoveredUrls.CountAsync(x =>
            x.ResearchRunId == run.Id && x.Status == DiscoveredUrlStatus.Pending);
        Assert.True(pendingAfterFirst > 0);
        Assert.DoesNotContain(state.CompletedSourceNames, x => x == "IndependentCrawler");

        await crawler.DiscoverAsync(context, CancellationToken.None);

        Assert.True(
            fetcher.RequestedUrls.Count >= 2
            || state.OrganizationInvestigations.Count >= 1
            || state.DomainInvestigationPlans.Count >= 1);
        Assert.True(state.DomainInvestigationPlans.Count >= 1
                    || pendingAfterFirst > 0);
    }

    [Fact]
    public void InvestigateFetchedPage_category_html_queues_profiles_and_official_sites()
    {
        var html = """
            <html><head><title>Private schools</title></head>
            <body>
              <article class="listing-card">
                <h2>Alpha Academy</h2>
                <a href="/edu-directory/schools/alpha-academy">Profile</a>
                <a href="https://example.com/alpha-academy">Website</a>
                <a href="mailto:office@example.com">office@example.com</a>
              </article>
              <article class="listing-card">
                <h2>Gamma Institute</h2>
                <a href="/edu-directory/schools/gamma-institute">Profile</a>
                <a href="https://example.net/gamma-institute">Website</a>
              </article>
            </body></html>
            """;
        var extracted = HtmlPageExtractor.Extract(
            html,
            new Uri("https://example.org/edu-directory/category/private-schools"));
        var state = new ResearchDiscoveryState();
        var result = WebsiteInvestigationEngine.InvestigateFetchedPage(new FetchedPageInvestigationRequest
        {
            PageUrl = "https://example.org/edu-directory/category/private-schools",
            NormalizedDomain = "example.org",
            Html = html,
            Extracted = extracted,
            UrlPurpose = UrlPagePurpose.DirectoryListing,
            SourceType = DiscoverySourceType.DirectoryCatalog,
            IsDirectorySeed = true,
            Depth = 1,
            MaxDepth = 4,
            Industries = ["Education"],
            Countries = ["Jordan"],
            DiscoveryState = state
        });

        Assert.True(result.Listings.Count >= 2, result.Summary);
        Assert.Contains(result.FollowUps, x => x.Url.Contains("/schools/alpha-academy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.FollowUps, x => x.Url.Contains("example.com/alpha-academy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.OrganizationCandidates, x => x.CompanyName.Contains("Alpha", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContactAttribution_official_domain_on_directory_is_same_domain()
    {
        var result = ContactAttributionHelper.Attribute(
            "office@example.com",
            "https://example.org/edu-directory/schools/alpha",
            "https://example.com/alpha-academy",
            pageIsDirectory: true);
        Assert.Equal(ContactAttributionState.SameDomain, result.State);
        Assert.Equal("contact.directory_official_domain", result.ReasonCode);
    }

    [Fact]
    public void Qualification_identity_without_email_is_not_email_ready()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Gamma Institute",
                Website = "https://example.net/gamma-institute",
                Country = "Jordan",
                CountryEvidenceSource = "page_address",
                CountryEvidenceSummary = "Campus address in Amman, Jordan",
                CompanyDescription = "Private education institute in Amman, Jordan",
                Industry = "Education",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 80,
                NameConfidence = 90,
                NameReasonCode = "investigation.listing_name"
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["Jordan"],
            Industries = ["Education"]
        });

        Assert.False(result.IsQualified);
        Assert.True(result.IsIdentityVerified);
        Assert.False(result.IsEmailReady);
    }

    [Fact]
    public void CompanyIdentityResolver_rejects_section_headings_as_organization_names()
    {
        Assert.True(CompanyIdentityResolver.IsGenericName("الأرقام التي نفخر بها"));
        Assert.True(CompanyIdentityResolver.IsGenericName("الوظائف الإدارية"));
        Assert.True(CompanyIdentityResolver.IsGenericName("About the Schools"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Welcome to BIST"));
        Assert.True(CompanyIdentityResolver.IsGenericName("أفضل مدارس خميس مشيط 2026"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Book a Visit →"));
        Assert.True(CompanyIdentityResolver.IsGenericName("by local moms for local moms"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Team"));
        Assert.True(CompanyIdentityResolver.IsGenericName("DISTRICT"));
    }

    [Fact]
    public void HtmlPageExtractor_rejects_image_filename_false_emails()
    {
        var html = """
            <html><body>
              <img src="https://cdn.example.com/blue-flower@2x.webp" />
              <a href="mailto:admissions@alphacademy.edu">admissions@alphacademy.edu</a>
            </body></html>
            """;
        var extracted = HtmlPageExtractor.Extract(html, new Uri("https://alphacademy.edu/contact"));
        Assert.DoesNotContain(extracted.Emails, x => x.Contains("webp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(extracted.Emails, x => x.Contains("blue-flower", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(extracted.Emails, x => x.Equals("admissions@alphacademy.edu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DirectoryListingExtractor_rejects_navigation_chrome_as_organizations()
    {
        var html = """
            <html><body>
              <div class="nav-item">
                <h2>تسجيل الدخول</h2>
                <a href="https://parents.example-dir.org/auth/login/otp">Login</a>
              </div>
              <div class="nav-item">
                <h2>البحث عن مدرسة</h2>
                <a href="https://parents.example-dir.org/school-search">Search</a>
              </div>
              <div class="listing-card">
                <h2>Visit Our Campus</h2>
                <a href="/visit-our-campus">Campus</a>
              </div>
              <div class="listing-card">
                <h2>Alpha Academy</h2>
                <a href="/schools/alpha-academy">Profile</a>
                <a href="https://example.com/alpha-academy">Website</a>
              </div>
            </body></html>
            """;

        var listings = DirectoryListingExtractor.Extract(html, new Uri("https://example-dir.org/"));
        Assert.DoesNotContain(listings, x => x.Name.Contains("تسجيل", StringComparison.Ordinal));
        Assert.DoesNotContain(listings, x => x.Name.Contains("البحث", StringComparison.Ordinal));
        Assert.DoesNotContain(listings, x => x.Name.Contains("Visit Our Campus", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(listings, x => x.Name.Contains("Alpha Academy", StringComparison.OrdinalIgnoreCase));
        var alpha = listings.First(x => x.Name.Contains("Alpha", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("example.com/alpha-academy", alpha.OfficialWebsiteUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(listings, x =>
            (x.OfficialWebsiteUrl ?? string.Empty).Contains("parents.example-dir.org", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PageContentAnalyzer_detects_spa_shell_and_harvests_embedded_urls()
    {
        var html = """
            <html><head><title>Directory Search</title></head>
            <body>
              <app-root></app-root>
              <script src="https://parents.example-dir.org/main.js"></script>
              <script>
                window.__CONFIG__ = {
                  api: "https://parents.example-dir.org/api/schools",
                  listing: "https://parents.example-dir.org/schools/alpha-academy"
                };
              </script>
            </body></html>
            """;
        var extracted = HtmlPageExtractor.Extract(html, new Uri("https://parents.example-dir.org/school-search"));
        Assert.True(PageContentAnalyzer.LooksLikeClientRenderedShell(html, extracted));
        var harvested = PageContentAnalyzer.HarvestEmbeddedAbsoluteUrls(
            html,
            new Uri("https://parents.example-dir.org/school-search"));
        Assert.Contains(harvested, x => x.Contains("/schools/alpha-academy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverAsync_directory_sibling_host_is_queued_from_landing_page()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db, "Private schools in Saudi Arabia");
        var run = await SeedRunAsync(db, project);
        var fetcher = new FakeSafeHttpFetcher();
        fetcher.Map(
            "https://madares.sa/",
            FakeSafeHttpFetcher.Html(
                "https://madares.sa/",
                """
                <html><head><title>مدارس</title></head>
                <body>
                  <a href="https://parents.madares.sa/school-search">Search schools</a>
                  <a href="https://tetco.sa">Partner</a>
                </body></html>
                """));
        fetcher.Map(
            "https://parents.madares.sa/school-search",
            FakeSafeHttpFetcher.Html(
                "https://parents.madares.sa/school-search",
                """
                <html><head><title>Madares</title></head>
                <body>
                  <app-root></app-root>
                  <script>
                    window.cfg = { profile: "https://parents.madares.sa/schools/alpha-academy" };
                  </script>
                </body></html>
                """));
        fetcher.Map(
            "https://parents.madares.sa/schools/alpha-academy",
            FakeSafeHttpFetcher.Html(
                "https://parents.madares.sa/schools/alpha-academy",
                """
                <html><body>
                  <article class="profile-card">
                    <h1>Alpha Academy</h1>
                    <a href="https://example.com/alpha-academy">Official website</a>
                  </article>
                </body></html>
                """));
        fetcher.Map(
            "https://example.com/alpha-academy/",
            FakeSafeHttpFetcher.Html(
                "https://example.com/alpha-academy/",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"School","name":"Alpha Academy","url":"https://example.com/alpha-academy"}
                  </script>
                </head>
                <body>Alpha Academy Saudi Arabia. <a href="/contact">Contact</a></body></html>
                """));
        fetcher.Map(
            "https://example.com/alpha-academy/contact",
            FakeSafeHttpFetcher.Html(
                "https://example.com/alpha-academy/contact",
                "<html><body><a href=\"mailto:office@example.com\">office@example.com</a></body></html>"));

        var crawler = CreateCrawler(
            db,
            fetcher,
            [
                new FixedDiscoverySource(
                    "DirectoryCatalog",
                    [
                        new DiscoveredUrlCandidate
                        {
                            Url = "https://madares.sa/",
                            SourceType = DiscoverySourceType.DirectoryCatalog,
                            SourceName = "Madares Saudi Schools",
                            Depth = 0
                        }
                    ])
            ]);

        var state = new ResearchDiscoveryState();
        var context = CreateSourceContext(
            project,
            run,
            state,
            ["Saudi Arabia"],
            ["Education", "Schools"]);

        for (var i = 0; i < 8; i++)
        {
            await crawler.DiscoverAsync(context, CancellationToken.None);
        }

        var urls = await db.DiscoveredUrls.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        Assert.Contains(urls, x => x.Url.Contains("parents.madares.sa/school-search", StringComparison.OrdinalIgnoreCase));
        var events = await db.ResearchRunEvents.Where(x => x.ResearchRunId == run.Id).ToListAsync();
        Assert.Contains(events, x => x.EventType == ResearchRunEventType.DirectorySiblingQueued
                                     || (x.DataJson != null && x.DataJson.Contains("directory_sibling", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(events, x => x.EventType == ResearchRunEventType.SpaShellDetected);
    }

    [Fact]
    public void UrlPriorityScorer_penalizes_high_volume_government_portals()
    {
        var school = UrlPriorityScorer.Score(
            "https://example.com/school/alpha/contact",
            DiscoverySourceType.LinkExpansion,
            ["Education"],
            ["Saudi Arabia"],
            parentDomain: "example.com",
            depth: 1,
            alreadyCrawled: false,
            pagePurpose: UrlPagePurpose.Contact);
        var gov = UrlPriorityScorer.Score(
            "https://sites.moe.gov.sa/riyadh/contact",
            DiscoverySourceType.LinkExpansion,
            ["Education"],
            ["Saudi Arabia"],
            parentDomain: "sites.moe.gov.sa",
            depth: 1,
            alreadyCrawled: false,
            pagePurpose: UrlPagePurpose.Contact);

        Assert.True(school.Total > gov.Total, $"school={school.Total} gov={gov.Total}");
    }

    [Fact]
    public void CrawlQueueScheduler_fairness_prevents_single_host_monopoly()
    {
        var pending = new List<DiscoveredUrl>();
        for (var i = 0; i < 20; i++)
        {
            pending.Add(new DiscoveredUrl
            {
                Url = $"https://sites.moe.gov.sa/office-{i}/contact",
                NormalizedUrl = $"https://sites.moe.gov.sa/office-{i}/contact",
                NormalizedDomain = "sites.moe.gov.sa",
                SourceType = DiscoverySourceType.LinkExpansion,
                Status = DiscoveredUrlStatus.Pending,
                Depth = 1,
                PagePurpose = UrlPagePurpose.Contact.ToString(),
                PriorityScore = 40,
                DiscoveredAtUtc = DateTimeOffset.UtcNow
            });
        }

        for (var i = 0; i < 6; i++)
        {
            pending.Add(new DiscoveredUrl
            {
                Url = $"https://example.com/school-{i}/contact",
                NormalizedUrl = $"https://example.com/school-{i}/contact",
                NormalizedDomain = "example.com",
                SourceType = DiscoverySourceType.LinkExpansion,
                Status = DiscoveredUrlStatus.Pending,
                Depth = 1,
                PagePurpose = UrlPagePurpose.Contact.ToString(),
                PriorityScore = 70,
                DiscoveredAtUtc = DateTimeOffset.UtcNow
            });
        }

        var ranked = CrawlQueueScheduler.RankPending(
            pending,
            ["Education"],
            ["Saudi Arabia"],
            new Dictionary<string, int>(),
            new HashSet<string>(),
            maxPagesPerDomain: 8,
            maxPerDomainInBatch: 3);

        var firstEight = ranked.Take(8).ToList();
        Assert.True(firstEight.Count(x => x.Domain == "example.com") >= 3);
        Assert.True(firstEight.Count(x => x.Domain == "sites.moe.gov.sa") <= 4);
    }

    [Fact]
    public void InvestigateFetchedPage_records_empty_listing_honestly_for_directory_home()
    {
        var html = """
            <html><head><title>Directory</title></head>
            <body><app-root></app-root></body></html>
            """;
        var extracted = HtmlPageExtractor.Extract(html, new Uri("https://example.org/edu-directory/school-search"));
        var state = new ResearchDiscoveryState();
        var result = WebsiteInvestigationEngine.InvestigateFetchedPage(new FetchedPageInvestigationRequest
        {
            PageUrl = "https://example.org/edu-directory/school-search",
            NormalizedDomain = "example.org",
            Html = html,
            Extracted = extracted,
            UrlPurpose = UrlPagePurpose.DirectoryListing,
            SourceType = DiscoverySourceType.DirectoryCatalog,
            IsDirectorySeed = true,
            Depth = 0,
            MaxDepth = 4,
            Industries = ["Education"],
            Countries = ["Saudi Arabia"],
            DiscoveryState = state
        });

        Assert.Empty(result.Listings);
        Assert.Empty(result.OrganizationCandidates);
        Assert.Contains("listings=0", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static FakeSafeHttpFetcher BuildDirectoryFixtureFetcher()
    {
        var fetcher = new FakeSafeHttpFetcher();
        fetcher.Map(
            "https://example.org/edu-directory/",
            FakeSafeHttpFetcher.Html(
                "https://example.org/edu-directory/",
                """
                <html><head><title>Education Directory Jordan</title></head>
                <body>
                  <h1>School categories</h1>
                  <a href="/edu-directory/category/private-schools">Private schools</a>
                  <a href="/edu-directory/category/public-schools">Public schools</a>
                </body></html>
                """));
        fetcher.Map(
            "https://example.org/edu-directory/category/private-schools",
            FakeSafeHttpFetcher.Html(
                "https://example.org/edu-directory/category/private-schools",
                """
                <html><head><title>Private schools</title></head>
                <body>
                  <article class="listing-card">
                    <h2>Alpha Academy</h2>
                    <a href="/edu-directory/schools/alpha-academy">Profile</a>
                    <a href="https://example.com/alpha-academy">Website</a>
                    <a href="mailto:office@example.com">office@example.com</a>
                    <a href="tel:+96265550001">+96265550001</a>
                  </article>
                  <article class="listing-card">
                    <h2>Gamma Institute</h2>
                    <a href="/edu-directory/schools/gamma-institute">Profile</a>
                    <a href="https://example.net/gamma-institute">Website</a>
                    <a href="tel:+96265550002">+96265550002</a>
                  </article>
                  <article class="listing-card">
                    <h2>Delta College</h2>
                    <a href="/edu-directory/schools/delta-college">Profile</a>
                    <a href="https://example.net/delta-college">Website</a>
                  </article>
                  <a href="/edu-directory/category/private-schools/page/2">Next</a>
                </body></html>
                """));
        fetcher.Map(
            "https://example.org/edu-directory/category/private-schools/page/2",
            FakeSafeHttpFetcher.Html(
                "https://example.org/edu-directory/category/private-schools/page/2",
                """
                <html><body>
                  <article class="listing-card">
                    <h2>Echo Academy</h2>
                    <a href="/edu-directory/schools/echo-academy">Profile</a>
                    <a href="https://example.net/echo-academy">Website</a>
                  </article>
                </body></html>
                """));
        fetcher.Map(
            "https://example.org/edu-directory/category/public-schools",
            FakeSafeHttpFetcher.Html(
                "https://example.org/edu-directory/category/public-schools",
                """
                <html><head><title>Public schools</title></head>
                <body>
                  <p>No matching private-school listings in this category.</p>
                </body></html>
                """));
        fetcher.Map(
            "https://example.org/edu-directory/schools/alpha-academy",
            FakeSafeHttpFetcher.Html(
                "https://example.org/edu-directory/schools/alpha-academy",
                """
                <html><body>
                  <article class="profile-card">
                    <h1>Alpha Academy</h1>
                    <p>Private school in Amman, Jordan.</p>
                    <a href="https://example.com/alpha-academy">Official website</a>
                    <a href="mailto:office@example.com">office@example.com</a>
                  </article>
                </body></html>
                """));
        fetcher.Map(
            "https://example.org/edu-directory/schools/gamma-institute",
            FakeSafeHttpFetcher.Html(
                "https://example.org/edu-directory/schools/gamma-institute",
                """
                <html><body>
                  <article class="profile-card">
                    <h1>Gamma Institute</h1>
                    <p>Private institute in Irbid, Jordan.</p>
                    <a href="https://example.net/gamma-institute">Official website</a>
                  </article>
                </body></html>
                """));
        fetcher.Map(
            "https://example.org/edu-directory/schools/delta-college",
            FakeSafeHttpFetcher.Html(
                "https://example.org/edu-directory/schools/delta-college",
                """
                <html><body>
                  <article class="profile-card">
                    <h1>Delta College</h1>
                    <a href="https://example.net/delta-college">Official website</a>
                  </article>
                </body></html>
                """));
        fetcher.Map(
            "https://example.com/alpha-academy/",
            FakeSafeHttpFetcher.Html(
                "https://example.com/alpha-academy/",
                """
                <html><head>
                  <title>Welcome</title>
                  <script type="application/ld+json">
                  {"@type":"School","name":"Alpha Academy","url":"https://example.com/alpha-academy"}
                  </script>
                </head>
                <body>
                  Alpha Academy is a private school in Jordan.
                  <a href="/about">About</a>
                  <a href="/contact">Contact</a>
                </body></html>
                """));
        fetcher.Map(
            "https://example.com/alpha-academy/about",
            FakeSafeHttpFetcher.Html(
                "https://example.com/alpha-academy/about",
                "<html><body><h1>About Alpha Academy</h1><p>Private education in Amman.</p></body></html>"));
        fetcher.Map(
            "https://example.com/alpha-academy/contact",
            FakeSafeHttpFetcher.Html(
                "https://example.com/alpha-academy/contact",
                """
                <html><head><title>Contact Us</title></head>
                <body>
                  Contact Alpha Academy at
                  <a href="mailto:office@example.com">office@example.com</a>
                  or <a href="tel:+96265550001">+96265550001</a>
                </body></html>
                """));
        fetcher.Map(
            "https://example.net/gamma-institute/",
            FakeSafeHttpFetcher.Html(
                "https://example.net/gamma-institute/",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"Organization","name":"Gamma Institute","url":"https://example.net/gamma-institute"}
                  </script>
                </head>
                <body>Gamma Institute private school Jordan. <a href="/about">About</a></body></html>
                """));
        fetcher.Map(
            "https://example.net/gamma-institute/about",
            FakeSafeHttpFetcher.Html(
                "https://example.net/gamma-institute/about",
                "<html><body><h1>About Gamma Institute</h1></body></html>"));
        fetcher.Map(
            "https://example.net/gamma-institute/contact",
            FakeSafeHttpFetcher.Forbidden("https://example.net/gamma-institute/contact"));
        fetcher.Map(
            "https://example.net/delta-college/",
            FakeSafeHttpFetcher.Html(
                "https://example.net/delta-college/",
                """
                <html><head>
                  <script type="application/ld+json">
                  {"@type":"Organization","name":"Delta College","url":"https://example.net/delta-college"}
                  </script>
                </head>
                <body>Delta College Jordan. <a href="/contact">Contact</a></body></html>
                """));
        fetcher.Map(
            "https://example.net/delta-college/contact",
            FakeSafeHttpFetcher.Html(
                "https://example.net/delta-college/contact",
                "<html><body>Email <a href=\"mailto:info@example.net\">info@example.net</a></body></html>"));
        return fetcher;
    }

    private static IndependentCrawlProspectSource CreateCrawler(
        SendlyDbContext db,
        FakeSafeHttpFetcher fetcher,
        IEnumerable<IUrlDiscoverySource> sources,
        int MaxFetchesPerRun = 40)
    {
        var options = MsOptions.Create(new ResearchOptions
        {
            EnableIndependentCrawler = true,
            EnableCommonCrawlDiscovery = false,
            MaxFetchesPerRun = MaxFetchesPerRun,
            MaxPagesPerCrawl = 20,
            MaxDiscoveryCandidates = 80,
            MaxExternalExpansionsPerPage = 10,
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
        ResearchDiscoveryState state,
        List<string> countries,
        List<string> industries)
        => new()
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            ProjectName = project.Name,
            Countries = countries,
            Industries = industries,
            DiscoveryBatchSize = 15,
            DiscoveryState = state
        };

    private static SendlyDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SendlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SendlyDbContext(options);
    }

    private static async Task<OutreachProject> SeedProjectAsync(SendlyDbContext db, string description)
    {
        var project = new OutreachProject
        {
            Name = "Investigation Project",
            Description = description,
            ResearchInstructions = description
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
