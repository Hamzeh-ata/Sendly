using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;

namespace Sendly.Application.Tests;

public class DiscoveryQualityRegressionTests
{
    [Fact]
    public void PriorResearchDiversity_keeps_fresh_when_prior_dominates()
    {
        var context = new UrlDiscoveryContext
        {
            MaxUrls = 20,
            MaxPriorResearchSharePercent = 40,
            PreferFreshSources = true,
            MinCandidatesBeforeRecovery = 10
        };
        var candidates = new List<DiscoveredUrlCandidate>();
        for (var i = 0; i < 16; i++)
        {
            candidates.Add(new DiscoveredUrlCandidate
            {
                Url = $"https://prior{i}.example/site",
                SourceType = DiscoverySourceType.PriorResearch,
                SourceName = "Prior",
                Depth = 0
            });
        }

        for (var i = 0; i < 4; i++)
        {
            candidates.Add(new DiscoveredUrlCandidate
            {
                Url = $"https://fresh{i}.example/",
                SourceType = DiscoverySourceType.DirectoryCatalog,
                SourceName = "Fresh",
                Depth = 0
            });
        }

        var result = DiscoveryAdmissionPolicy.ApplyPriorResearchDiversity(candidates, context);
        var prior = result.Count(x => x.SourceType == DiscoverySourceType.PriorResearch);
        var fresh = result.Count(x => x.SourceType != DiscoverySourceType.PriorResearch);
        Assert.Equal(4, fresh);
        Assert.True(prior <= 4);
        Assert.True(fresh + prior <= 20);
    }

    [Fact]
    public void OfficialDomainRecovery_creates_candidates_from_directory_links()
    {
        var result = OfficialDomainRecovery.BuildCandidates(
            [
                "https://acme-clean.example/",
                "https://beta-clean.example/contact",
                "https://www.yell.com/listing",
                "https://cdn.example/app.js",
                "https://acme-clean.example/careers"
            ],
            parentUrl: "https://directory.example/list",
            knownDomains: ["directory.example"],
            knownCanonicalUrls: [],
            industries: ["Cleaning"],
            countries: ["United Kingdom"],
            maxCandidates: 10);

        Assert.Contains(result.Candidates, x => x.Url.Contains("acme-clean.example", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Candidates, x => x.Url.Contains("beta-clean.example", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Candidates, x => x.Url.Contains("yell.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Candidates, x => x.Url.Contains("/careers", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("recovery.official_domains_added", result.OutcomeReason);
    }

    [Fact]
    public void Identity_rejects_listicle_and_jobs_titles()
    {
        Assert.True(CompanyIdentityResolver.IsGenericName("Find a School"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Employment Opportunities"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Boarding Staff Jobs in Independent Schools"));
        Assert.True(CompanyIdentityResolver.IsGenericName("The Best 160 Schools In Bristol"));
        Assert.True(EntityClassifier.IsListicleName("International Schools in Edinburgh, United Kingdom"));

        var jobs = EntityClassifier.Classify(
            "Employment Opportunities",
            "Employment Opportunities",
            "Join our team",
            "https://www.tasisengland.org/careers");
        Assert.NotEqual(ResearchEntityType.TargetBusiness, jobs.EntityType);

        var find = EntityClassifier.Classify(
            "Find a School",
            "Find a School",
            "Search independent schools",
            "https://www.isc.co.uk/schools/");
        Assert.NotEqual(ResearchEntityType.TargetBusiness, find.EntityType);
    }

    [Fact]
    public void ContactAttribution_rejects_third_party_page_emails()
    {
        var rejected = ContactAttributionHelper.Attribute(
            "mail@isllondon.org",
            "https://www.teacherhorizons.com/jobs/isl",
            "https://www.teacherhorizons.com",
            pageIsDirectory: false);
        Assert.Equal(ContactAttributionState.Rejected, rejected.State);

        var same = ContactAttributionHelper.Attribute(
            "hello@acme.example",
            "https://acme.example/contact",
            "https://acme.example",
            pageIsDirectory: false);
        Assert.Equal(ContactAttributionState.SameDomain, same.State);

        var directory = ContactAttributionHelper.Attribute(
            "ads@directory.example",
            "https://directory.example/list",
            "https://acme.example",
            pageIsDirectory: true);
        Assert.Equal(ContactAttributionState.DirectoryListing, directory.State);
    }

    [Fact]
    public void UrlAdmission_classifies_jobs_and_listicle_paths()
    {
        Assert.Equal(UrlPagePurpose.JobListing, UrlAdmissionPolicy.ClassifyPagePurpose("https://example.com/careers"));
        Assert.Equal(UrlPagePurpose.Listicle, UrlAdmissionPolicy.ClassifyPagePurpose("https://example.com/best-schools-in-london"));
        Assert.Equal(UrlPagePurpose.Navigation, UrlAdmissionPolicy.ClassifyPagePurpose("https://example.com/find-a-school"));
        Assert.True(UrlAdmissionPolicy.IsLowValueExpansionPurpose(UrlPagePurpose.JobListing));
        Assert.False(UrlAdmissionPolicy.IsExpansionRoot(UrlPagePurpose.JobListing));
    }

    [Fact]
    public void Identity_rejects_directory_ui_chrome_without_website()
    {
        Assert.True(CompanyIdentityResolver.IsGenericName("اتفاقية مستوى الخدمة"));
        Assert.True(CompanyIdentityResolver.IsGenericName("الاستعلام عن طلبات التسجيل"));
        Assert.True(CompanyIdentityResolver.IsGenericName("ق المرئيات تطبي نوع المستفيد مرئيات المستفيدين # حقل السجل"));

        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "اتفاقية مستوى الخدمة",
                Website = null,
                Country = "Saudi Arabia",
                Industry = "Education",
                SourceUrl = "https://madares.sa/index-en.html",
                NameConfidence = 60,
                NameReasonCode = "identity.inferred",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 55
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["Saudi Arabia"],
            Industries = ["Education", "School"]
        });

        Assert.False(result.IsQualified);
        Assert.False(result.IsIdentityVerified);
        Assert.Contains("companyIdentity", result.MissingCriteria);
        Assert.Contains("officialWebsite", result.MissingCriteria);
    }

    [Fact]
    public void Qualification_rejects_cross_domain_contact_attribution()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Acme Cleaning Co",
                Website = "https://acme.example",
                Country = "Saudi Arabia",
                Industry = "Cleaning",
                Email = "hello@pressburst.com",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 80,
                NameConfidence = 90,
                NameReasonCode = "identity.json_ld_organization",
                ContactAttributionState = ContactAttributionState.CrossDomainEvidence,
                ContactAttributionConfidence = 35,
                ContactAttributionReasonCode = "contact.cross_domain"
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["Saudi Arabia"],
            Industries = ["Cleaning"]
        });

        Assert.False(result.IsEmailReady);
        Assert.Contains("contactAttribution", result.MissingCriteria);
    }
}
