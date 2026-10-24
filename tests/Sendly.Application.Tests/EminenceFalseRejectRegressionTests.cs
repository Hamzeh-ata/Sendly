using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Tests;

public class EminenceFalseRejectRegressionTests
{
    [Fact]
    public void Classify_does_not_treat_award_copy_all_schools_as_navigation()
    {
        var name = "Eminence Private School";
        var desc = "We are pleased to inform you that we have been awarded 3rd place across all schools in the United Arab Emirates (and only school from Fujairah) for its exemplary student well-being initiatives. This award was received from the Ministry of Health and Prevention (MOHAP), under the Masar Project.";
        var website = "https://www.eminenceschool.org";

        var classified = EntityClassifier.Classify(name, name, desc, website);

        Assert.Equal(ResearchEntityType.TargetBusiness, classified.EntityType);
        Assert.True(EntityClassifier.IsAllowedTarget(classified.EntityType, ResearchEntityType.TargetBusiness));
    }

    [Fact]
    public void Qualify_accepts_search_candidate_with_same_domain_email_and_website()
    {
        var service = new ProspectQualificationService();
        var candidate = new ProspectCandidate
        {
            CompanyName = "Eminence Private School",
            Website = "https://www.eminenceschool.org",
            Country = "UAE",
            City = "Fujairah",
            Industry = "Private School",
            CompanyDescription = "Private school in Fujairah, United Arab Emirates. Awarded 3rd place across all schools in the United Arab Emirates.",
            Email = "info@eminenceschool.org",
            Phone = "+971 9 222 0404",
            SourceUrl = "https://www.eminenceschool.org/",
            SourceType = ProspectSourceType.Search,
            SourceName = "SearXNG"
        };

        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            Candidate = candidate,
            Countries = ["UAE", "United Arab Emirates"],
            Industries = ["Private School", "Independent School"],
            MinQualificationScore = 80
        });

        Assert.DoesNotContain("entityType", result.MissingCriteria);
        Assert.DoesNotContain("companyIdentity", result.MissingCriteria);
        Assert.DoesNotContain("contactAttribution", result.MissingCriteria);
        Assert.Equal(ContactAttributionState.SameDomain, candidate.ContactAttributionState);
        Assert.True(candidate.NameConfidence >= 55);
        Assert.True(result.Score >= 80, $"Expected score >= 80, got {result.Score}. Summary={result.Summary}");
        Assert.True(result.IsQualified);
    }
}
