using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;

namespace Sendly.Application.Tests;

public class DiscoveryBenchmarkAndRecoveryTests
{
    [Fact]
    public void SourceExpansionScorer_UsesLinkTextAndIndustrySignals()
    {
        var score = SourceExpansionScorer.Score(
            "https://example.com/page",
            linkText: "Member Directory",
            surroundingContext: "Find commercial cleaning companies",
            pageTitle: "Association members",
            pageExcerpt: "Browse our members list of cleaning companies in Saudi Arabia",
            industries: ["Cleaning"],
            countries: ["Saudi Arabia"]);

        Assert.True(score.LooksPromising);
        Assert.True(score.LinkTextSignal > 0);
        Assert.True(score.IndustrySignal > 0);
        Assert.True(score.GeographySignal > 0);
    }

    [Fact]
    public void AdaptiveDiscoveryRecovery_NoCandidates_BroadensTerminology()
    {
        var context = new UrlDiscoveryContext
        {
            MinCandidatesBeforeRecovery = 10,
            Industries = ["Cleaning"],
            IndustrySynonyms = ["نظافة", "Janitorial"],
            Countries = ["Saudi Arabia"]
        };
        var current = new AutomaticDiscoveryResult();
        var action = AdaptiveDiscoveryRecovery.Decide(context, current, duplicateRatePercent: 0, failedFetchPercent: 0);

        Assert.NotNull(action);
        Assert.Equal("BroadenTerminology", action!.Strategy);
        AdaptiveDiscoveryRecovery.Apply(context, action);
        Assert.Contains(context.Industries, x => x.Contains("نظافة", StringComparison.Ordinal));
        Assert.Contains(context.Countries, x => x.Equals("KSA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AdaptiveDiscoveryRecovery_HighDuplicates_PrefersUnexplored()
    {
        var context = new UrlDiscoveryContext { MinCandidatesBeforeRecovery = 20 };
        var current = new AutomaticDiscoveryResult
        {
            Candidates =
            [
                new DiscoveredUrlCandidate { Url = "https://example.com/a" },
                new DiscoveredUrlCandidate { Url = "https://example.com/b" }
            ]
        };

        var action = AdaptiveDiscoveryRecovery.Decide(context, current, duplicateRatePercent: 55, failedFetchPercent: 0);

        Assert.NotNull(action);
        Assert.Equal("PreferUnexploredSources", action!.Strategy);
        Assert.True(action.PreferUnexploredProviders);
    }

    [Fact]
    public void DiscoveryBenchmarkCalculator_ComputesRecallOnlyWhenDefined()
    {
        var reference = new List<DiscoveryBenchmarkSample>
        {
            new() { CompanyName = "Alpha Clean", WebsiteDomain = "alpha.example", IsRelevant = true },
            new() { CompanyName = "Beta Clean", WebsiteDomain = "beta.example", IsRelevant = true },
            new() { CompanyName = "Gamma Clean", WebsiteDomain = "gamma.example", IsRelevant = true }
        };
        var observations = new List<DiscoveryBenchmarkObservation>
        {
            new()
            {
                CompanyName = "Alpha Clean",
                WebsiteDomain = "alpha.example",
                CorrectlyIdentified = true,
                Qualified = true,
                LiveWebsiteVerified = true,
                PublicContactWithEvidence = true
            },
            new()
            {
                CompanyName = "Alpha Clean",
                WebsiteDomain = "alpha.example",
                CorrectlyIdentified = true,
                IsDuplicate = true
            },
            new()
            {
                CompanyName = "Noise Co",
                WebsiteDomain = "noise.example",
                IsFalsePositive = true
            }
        };

        var withRecall = DiscoveryBenchmarkCalculator.Evaluate(
            "Cleaning-SA-fixture",
            reference,
            observations,
            elapsedMilliseconds: 1200,
            limitations: "Fixture sample of 3 reference companies; not market coverage.",
            recallIsDefined: true);

        Assert.Equal(2, withRecall.UniqueCompaniesDiscovered);
        Assert.Equal(1, withRecall.CorrectlyIdentified);
        Assert.Equal(1, withRecall.QualifiedCompanies);
        Assert.Equal(1, withRecall.DuplicateCount);
        Assert.Equal(1, withRecall.FalsePositiveCount);
        Assert.Equal(0.3333, withRecall.Recall);
        Assert.Contains("not market coverage", withRecall.Limitations);

        var withoutRecall = DiscoveryBenchmarkCalculator.Evaluate(
            "Cleaning-SA-fixture",
            reference,
            observations,
            1200,
            "Undefined universe",
            recallIsDefined: false);
        Assert.Null(withoutRecall.Recall);
    }

    [Fact]
    public void DiscoveryOutcomeClassifier_DistinguishesMissingEvidenceFromNoCompanies()
    {
        var missingEvidence = new ResearchDiscoveryState
        {
            CandidatesDiscovered = 4,
            CandidatesValidated = 3
        };
        missingEvidence.RecordRejection("Missing public business email");

        Assert.Equal(
            DiscoveryStopReason.MissingRequiredEvidence,
            DiscoveryOutcomeClassifier.Classify(missingEvidence, 0, 10));

        var noCompanies = new ResearchDiscoveryState
        {
            CandidatesDiscovered = 5,
            CandidatesValidated = 0
        };
        Assert.Equal(
            DiscoveryStopReason.NoCompaniesIdentified,
            DiscoveryOutcomeClassifier.Classify(noCompanies, 0, 10));
    }

    [Fact]
    public void DiscoveryBenchmarkFixtures_CoverMultipleIndustriesAndCountries()
    {
        var fixtures = DiscoveryBenchmarkFixtures.All();
        Assert.True(fixtures.Count >= 3);
        Assert.Contains(fixtures, x => x.Country.Contains("Saudi", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fixtures, x => x.Country.Contains("Emirates", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fixtures, x => x.Country.Contains("Egypt", StringComparison.OrdinalIgnoreCase));
        Assert.All(fixtures, x =>
        {
            Assert.False(string.IsNullOrWhiteSpace(x.Limitations));
            Assert.True(x.Reference.Count >= 3);
            var result = DiscoveryBenchmarkCalculator.Evaluate(
                x.Name,
                x.Reference,
                x.Reference.Select(r => new DiscoveryBenchmarkObservation
                {
                    CompanyName = r.CompanyName,
                    WebsiteDomain = r.WebsiteDomain,
                    CorrectlyIdentified = true,
                    Qualified = r.HasPublicContact,
                    LiveWebsiteVerified = true,
                    PublicContactWithEvidence = r.HasPublicContact
                }).ToList(),
                elapsedMilliseconds: 100,
                limitations: x.Limitations,
                recallIsDefined: true);
            Assert.Equal(1.0, result.Recall);
            Assert.Contains("not", result.Limitations!, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void LiveVerificationHelper_RequiresIdentitySupportInPageContent()
    {
        Assert.True(LiveVerificationHelper.SupportsCompanyIdentity(
            "Riyadh Facility Clean",
            "Riyadh Facility Clean | Home",
            "Welcome to Riyadh Facility Clean services"));
        Assert.False(LiveVerificationHelper.SupportsCompanyIdentity(
            "Riyadh Facility Clean",
            "Yellow Pages Directory",
            "Browse local businesses"));
    }

    [Fact]
    public void LiveVerificationHelper_PrioritizesContactPages()
    {
        var ranked = LiveVerificationHelper.PrioritizeContactPages(
        [
            "https://example.com/services",
            "https://example.com/contact-us",
            "https://example.com/about"
        ]);

        Assert.Equal("https://example.com/contact-us", ranked[0]);
    }

    [Fact]
    public void AdaptiveDiscoveryRecovery_Apply_EnablesContactHintsAndUnexploredProviders()
    {
        var context = new UrlDiscoveryContext
        {
            Industries = ["Cleaning"],
            IndustrySynonyms = ["Janitorial"],
            Countries = ["Saudi Arabia"]
        };
        var action = new AdaptiveRecoveryAction
        {
            ExpandIndustrySynonyms = true,
            PreferUnexploredProviders = true,
            EnableContactPageHints = true,
            DeprioritizeDeadDomains = true
        };

        AdaptiveDiscoveryRecovery.Apply(context, action);

        Assert.True(context.PreferUnexploredProviders);
        Assert.True(context.EnableContactPageHints);
        Assert.True(context.DeprioritizeDeadDomains);
        Assert.Contains("Janitorial", context.Industries);
    }
}
