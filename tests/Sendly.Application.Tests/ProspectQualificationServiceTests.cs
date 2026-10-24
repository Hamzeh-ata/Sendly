using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;

namespace Sendly.Application.Tests;

public class ProspectQualificationServiceTests
{
    [Fact]
    public void Evaluate_tracks_missing_email_without_erasing_target_fit()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            Candidate = new ProspectCandidate
            {
                CompanyName = "Acme Cleaning Services",
                Website = "https://acme.example",
                Country = "Saudi Arabia",
                CountryEvidenceSource = "page_address",
                CountryEvidenceSummary = "Address lists Riyadh, Saudi Arabia",
                Industry = "Cleaning",
                CompanyDescription = "Commercial cleaning company in Riyadh, Saudi Arabia",
                EntityType = Domain.Enums.ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 70,
                NameConfidence = 80,
                NameReasonCode = "identity.title_corroborated"
            },
            QueryVersion = new ResearchQueryVersion
            {
                RequireOfficialWebsite = true,
                RequirePublicBusinessEmail = true,
                TargetCountry = "Saudi Arabia",
                TargetIndustry = "Cleaning"
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["Saudi Arabia"],
            Industries = ["Cleaning"]
        });

        Assert.Contains("publicBusinessEmail", result.MissingCriteria);
        Assert.DoesNotContain(
            result.Criteria,
            x => x.CriterionKey == "publicBusinessEmail" && x.IsMandatory);
        Assert.False(result.IsQualified);
        Assert.True(result.Score >= 60);
        Assert.Contains(result.Criteria, x => x.CriterionKey == "targetIndustry" && x.Passed);
    }

    [Fact]
    public void Evaluate_mandatory_failure_caps_score_below_threshold()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            Candidate = new ProspectCandidate
            {
                CompanyName = "Best Gyms Near Me Directory",
                Website = "https://directory.example",
                Country = "New Zealand",
                Industry = "Gym",
                Email = "info@directory.example",
                EntityType = Domain.Enums.ResearchEntityType.DirectoryAggregator,
                EntityTypeConfidence = 90,
                NameConfidence = 80
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["New Zealand"],
            Industries = ["Gym"]
        });

        Assert.False(result.IsQualified);
        Assert.Contains("entityType", result.MissingCriteria);
        Assert.True(result.Score < 60);
    }

    [Fact]
    public void Evaluate_passes_when_mandatory_criteria_met()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            Candidate = new ProspectCandidate
            {
                CompanyName = "Acme Cleaning",
                Website = "https://acme.example",
                Country = "Saudi Arabia",
                CountryEvidenceSource = "page_address",
                CountryEvidenceSummary = "Office address in Riyadh, Saudi Arabia",
                Industry = "Commercial cleaning",
                CompanyDescription = "Commercial cleaning company operating in Saudi Arabia",
                Email = "info@acme.example",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 80,
                NameConfidence = 90,
                NameReasonCode = "identity.json_ld_organization",
                ContactAttributionState = ContactAttributionState.SameDomain,
                ContactAttributionConfidence = 90,
                ContactAttributionReasonCode = "contact.same_domain"
            },
            QueryVersion = new ResearchQueryVersion
            {
                RequireOfficialWebsite = true,
                RequirePublicBusinessEmail = true,
                TargetCountry = "Saudi Arabia",
                TargetIndustry = "cleaning"
            },
            RequireEmail = true,
            MinQualificationScore = 50,
            Countries = ["Saudi Arabia"],
            Industries = ["cleaning"],
            Keywords = ["cleaning"]
        });

        Assert.True(result.IsQualified);
        Assert.True(result.Score >= 50);
        Assert.Contains(result.Criteria, x => x.CriterionKey == "publicBusinessEmail" && x.Passed);
    }
}
