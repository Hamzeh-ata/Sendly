using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;

namespace Sendly.Application.Tests;

public class CrawlerQualityRegressionTests
{
    [Fact]
    public void CompanyIdentity_rejects_generic_titles()
    {
        var result = CompanyIdentityResolver.Resolve("Home", "Welcome to our site", null, null);
        Assert.False(result.IsAcceptable);
        Assert.Equal("identity.generic_or_missing", result.ReasonCode);
        Assert.True(CompanyIdentityResolver.IsGenericName("A guide to all private schools"));
    }

    [Fact]
    public void CompanyIdentity_accepts_corroborated_title()
    {
        var result = CompanyIdentityResolver.Resolve(
            "Acme Property Management | Home",
            "Acme Property Management helps landlords collect rent.",
            "acme-property.ae",
            "https://acme-property.ae");
        Assert.True(result.IsAcceptable);
        Assert.Equal("Acme Property Management", result.CompanyName);
    }

    [Fact]
    public void EntityClassifier_marks_directory_hosts()
    {
        var result = EntityClassifier.Classify(
            "United States Online Business Directory",
            "Directory of businesses",
            "Find companies in our business directory listings",
            "https://bizdir24.com");
        Assert.Equal(ResearchEntityType.DirectoryAggregator, result.EntityType);
        Assert.False(EntityClassifier.IsAllowedTarget(result.EntityType, ResearchEntityType.TargetBusiness));
    }

    [Fact]
    public void EntityClassifier_rejects_encyclopedia_topic_articles()
    {
        var result = EntityClassifier.Classify(
            "التعليم في مدينة نجران",
            "التعليم في مدينة نجران",
            "Overview of education institutions in the city.",
            "https://saudipedia.com/التعليم-في-مدينة-نجران");
        Assert.Equal(ResearchEntityType.PublisherEditorial, result.EntityType);
        Assert.False(EntityClassifier.IsAllowedTarget(result.EntityType, ResearchEntityType.TargetBusiness));
        Assert.True(EntityClassifier.IsTopicArticleName("التعليم في مدينة نجران"));
    }

    [Fact]
    public void CompanyIdentity_rejects_directory_hosts_as_official_domain()
    {
        var result = CompanyIdentityResolver.Resolve(
            "Trbyh Namouthajiya National Schools - Buraydah",
            "School listing page on a directory.",
            "yaschools.com",
            "https://yaschools.com/المدارس/نجران");
        Assert.False(result.IsAcceptable);
        Assert.Equal("identity.directory_host", result.ReasonCode);
        Assert.True(SystemDirectoryCatalog.IsSeedOrDirectoryHost("ksaschools.com"));
    }

    [Fact]
    public void Qualification_rejects_directory_website_instead_of_needs_review()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            Candidate = new ProspectCandidate
            {
                CompanyName = "Trbyh Namouthajiya National Schools - Buraydah",
                Website = "https://yaschools.com",
                SourceUrl = "https://yaschools.com/المدارس/نجران",
                Country = "Saudi Arabia",
                Industry = "Education",
                CompanyDescription = "Directory listing for a school.",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 55,
                NameConfidence = 80,
                NameReasonCode = "identity.title_corroborated"
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["Saudi Arabia"],
            Industries = ["Education"]
        });

        Assert.False(result.IsQualified);
        Assert.False(result.IsIdentityVerified);
        Assert.Contains("officialWebsite", result.MissingCriteria);
        Assert.Contains("companyIdentity", result.MissingCriteria);
    }

    [Fact]
    public void UrlPriority_penalizes_unrelated_country_hosts()
    {
        var score = UrlPriorityScorer.Score(
            "https://randstad.com.au/jobs",
            DiscoverySourceType.LinkExpansion,
            ["Property Management"],
            ["United Arab Emirates"],
            "yellowpages.ae",
            depth: 2,
            alreadyCrawled: false);
        Assert.True(score.CostPenalty >= 40);
        Assert.False(score.IsDiscoveryUseful);
    }

    [Fact]
    public void UrlPriority_prefers_contact_pages_on_same_domain()
    {
        var score = UrlPriorityScorer.Score(
            "https://acme.ae/contact",
            DiscoverySourceType.LinkExpansion,
            ["Property Management"],
            ["United Arab Emirates"],
            "acme.ae",
            depth: 1,
            alreadyCrawled: false);
        Assert.True(score.IsVerificationUseful);
        Assert.True(score.Total >= 40);
    }

    [Fact]
    public void ContactAttribution_rejects_page_operator_email_mismatch()
    {
        var result = ContactAttributionHelper.Attribute(
            "info@findmyschool.com.au",
            "https://findmyschool.com.au/school/abc",
            "https://stgeorges.wa.edu.au",
            pageIsDirectory: false);
        Assert.Equal(ContactAttributionState.Rejected, result.State);
    }

    [Fact]
    public void ContactAttribution_accepts_same_domain_email()
    {
        var result = ContactAttributionHelper.Attribute(
            "info@acme.ae",
            "https://acme.ae/contact",
            "https://acme.ae",
            pageIsDirectory: false);
        Assert.Equal(ContactAttributionState.SameDomain, result.State);
        Assert.True(result.Confidence >= 80);
    }

    [Fact]
    public void Qualification_rejects_directory_even_with_high_keyword_overlap()
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
                EntityTypeReasonCode = "entity.directory_signals",
                NameConfidence = 80,
                NameReasonCode = "identity.title_only"
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["Australia"],
            Industries = ["Independent School", "Private School"],
            Keywords = ["school", "private school"]
        });

        Assert.False(result.IsQualified);
        Assert.Contains("entityType", result.MissingCriteria);
    }

    [Fact]
    public void Qualification_rejects_missing_industry_evidence()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Generic Co",
                Website = "https://generic.ae",
                Country = "United Arab Emirates",
                Email = "hello@generic.ae",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 70,
                NameConfidence = 70,
                ContactAttributionState = ContactAttributionState.SameDomain
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["United Arab Emirates"],
            Industries = ["Property Management", "Landlords"]
        });

        Assert.False(result.IsQualified);
        Assert.Contains("targetIndustry", result.MissingCriteria);
    }

    [Fact]
    public void DiscoveryBudget_separates_fetch_from_discovery()
    {
        var options = new Options.ResearchOptions
        {
            MaxCandidatesPerRun = 200,
            MaxDiscoveryCandidates = 150,
            MaxFetchesPerRun = 40,
            MaxPagesPerCrawl = 5,
            MaxCandidateDomains = 100,
            MaxPriorResearchSharePercent = 40,
            MaxExternalExpansionsPerPage = 5
        };
        var budget = DiscoveryBudgetResolver.Resolve(options, null);
        Assert.Equal(150, budget.MaxDiscoveryCandidates);
        Assert.Equal(40, budget.MaxFetchesPerRun);
        Assert.NotEqual(budget.MaxDiscoveryCandidates, budget.MaxFetchesPerRun);
    }
}
