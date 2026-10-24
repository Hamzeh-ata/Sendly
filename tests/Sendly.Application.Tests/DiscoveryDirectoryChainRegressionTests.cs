using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Application.Services.WebsiteInvestigation;
using Sendly.Domain.Enums;

namespace Sendly.Application.Tests;

public class DiscoveryDirectoryChainRegressionTests
{
    [Fact]
    public void D_ValidDirectoryChain_ExtractsOrgsAndQueuesOfficialSites()
    {
        var html = """
                   <html><body>
                   <nav><a href="/membership">Membership</a><a href="/events">Events</a></nav>
                   <article class="listing-card">
                     <h2>Aurora Private School</h2>
                     <a href="/schools/aurora">Profile</a>
                     <a href="https://aurora-school.sa">Website</a>
                   </article>
                   <article class="listing-card">
                     <h2>Beacon Independent School</h2>
                     <a href="/schools/beacon">Profile</a>
                     <a href="https://beacon-school.sa">Website</a>
                   </article>
                   </body></html>
                   """;
        var pageUrl = "https://schools.example.sa/directory";
        var extracted = HtmlPageExtractor.Extract(html, new Uri(pageUrl));
        var state = new ResearchDiscoveryState();
        var result = WebsiteInvestigationEngine.InvestigateFetchedPage(new FetchedPageInvestigationRequest
        {
            PageUrl = pageUrl,
            NormalizedDomain = "schools.example.sa",
            Html = html,
            Extracted = extracted,
            UrlPurpose = UrlPagePurpose.DirectoryListing,
            SourceType = DiscoverySourceType.DirectoryCatalog,
            IsDirectorySeed = true,
            Depth = 0,
            MaxDepth = 4,
            Industries = ["Private School", "Independent School"],
            Countries = ["Saudi Arabia"],
            DiscoveryState = state
        });

        Assert.True(result.Listings.Count >= 2, result.Summary);
        Assert.Contains(result.OrganizationCandidates, x =>
            x.CompanyName.Contains("Aurora", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.FollowUps, x =>
            x.Url.Contains("aurora-school.sa", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.FollowUps, x =>
            x.Url.Contains("/schools/aurora", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Listings, x =>
            x.Name.Equals("Membership", StringComparison.OrdinalIgnoreCase)
            || x.Name.Equals("Events", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void F_EmailOnUnrelatedPage_IsNotAttributedViaDirectoryChrome()
    {
        var html = """
                   <html><body>
                   <div class="listing-card">
                     <h3>Membership</h3>
                     <a href="mailto:info@unrelated.com">info@unrelated.com</a>
                   </div>
                   <div class="listing-card">
                     <h3><a href="/schools/real">Real Academy</a></h3>
                     <a href="https://real-academy.edu">Site</a>
                   </div>
                   </body></html>
                   """;

        var listings = DirectoryListingExtractor.Extract(
            html,
            new Uri("https://directory.example/list"));

        Assert.DoesNotContain(listings, x => x.Name.Equals("Membership", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(listings, x => x.Name.Contains("Real Academy", StringComparison.OrdinalIgnoreCase));
        var real = listings.First(x => x.Name.Contains("Real Academy", StringComparison.OrdinalIgnoreCase));
        Assert.True(string.IsNullOrWhiteSpace(real.Email) || real.Email!.Contains("real-academy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void G_OffIndustryHosts_ArePenalizedInPriority()
    {
        var score = UrlPriorityScorer.Score(
            "https://www.issa.com/membership/levels/",
            DiscoverySourceType.LinkExpansion,
            ["Private School", "Independent School"],
            ["United States"],
            parentDomain: "issa.com",
            depth: 1,
            alreadyCrawled: false,
            pagePurpose: UrlPagePurpose.DirectoryListing);

        Assert.Contains("off_industry", score.ReasonCode, StringComparison.OrdinalIgnoreCase);
        Assert.True(score.CostPenalty >= 45);
    }

    [Fact]
    public void H_Deduplicator_MergesSameOrgPreservingEvidence()
    {
        var merged = ProspectCandidateDeduplicator.DeduplicateAndMerge(
        [
            new ProspectCandidate
            {
                CompanyName = "Aurora Private School",
                Website = "https://aurora-school.edu",
                SourceUrl = "https://schools.example/schools/aurora",
                NameConfidence = 70,
                Email = null
            },
            new ProspectCandidate
            {
                CompanyName = "Aurora Private School",
                Website = "https://aurora-school.edu/",
                SourceUrl = "https://aurora-school.edu/contact",
                NameConfidence = 90,
                Email = "info@aurora-school.edu"
            }
        ]);

        Assert.Single(merged);
        Assert.Equal("info@aurora-school.edu", merged[0].Email);
        Assert.True(merged[0].NameConfidence >= 90);
    }

}
