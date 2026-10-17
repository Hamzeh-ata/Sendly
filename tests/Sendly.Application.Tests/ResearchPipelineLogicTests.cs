using Sendly.Application.Abstractions;
using Sendly.Application.Services;

namespace Sendly.Application.Tests;

public class ResearchPipelineLogicTests
{
    [Fact]
    public void Deduplicate_SimilarNamesWithoutStrongEvidence_KeepsBoth()
    {
        var merged = ProspectCandidateDeduplicator.DeduplicateAndMerge(
        [
            new ProspectCandidate
            {
                CompanyName = "ABC Fitness",
                City = "Toronto",
                SourceUrl = "https://abc-fitness-a.example",
                SourceName = "Search"
            },
            new ProspectCandidate
            {
                CompanyName = "ABC Fitness Centre",
                City = "Toronto",
                SourceUrl = "https://abc-fitness-b.example",
                SourceName = "Search"
            }
        ]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void Deduplicate_SameDomain_MergesCandidates()
    {
        var merged = ProspectCandidateDeduplicator.DeduplicateAndMerge(
        [
            new ProspectCandidate
            {
                CompanyName = "Iron Gym",
                Website = "https://irongym.ca",
                SourceUrl = "https://irongym.ca",
                SourceName = "Search"
            },
            new ProspectCandidate
            {
                CompanyName = "Iron Gym Toronto",
                Website = "https://www.irongym.ca",
                Email = "hello@irongym.ca",
                SourceUrl = "https://irongym.ca/contact",
                SourceName = "Website"
            }
        ]);

        Assert.Single(merged);
        Assert.Equal("hello@irongym.ca", merged[0].Email);
    }

    [Fact]
    public async Task StructureFromSourceText_WithoutEmailOrWhatsApp_StillReturnsCandidate()
    {
        var ai = new RuleBasedAiResearchService();
        var candidates = await ai.StructureFromSourceTextAsync(
            new AiSourceStructureRequest
            {
                ProjectName = "GymWatch",
                HintCompanyName = "Peak Performance Gym",
                HintCountry = "Canada",
                HintIndustry = "Fitness",
                SourceUrl = "https://peakperformance.example",
                SourceName = "Website",
                PageText = "Peak Performance Gym is a physical gym in Toronto with memberships and trainers.",
                ExtractedEmails = [],
                ExtractedPhones = []
            },
            CancellationToken.None);

        Assert.Single(candidates);
        Assert.Equal("Peak Performance Gym", candidates[0].CompanyName);
        Assert.Null(candidates[0].Email);
        Assert.Null(candidates[0].Phone);
    }

    [Fact]
    public async Task Qualify_MissingEmailAndWhatsApp_CanStillBeRelevant()
    {
        var ai = new RuleBasedAiResearchService();
        var result = await ai.QualifyAsync(
            new AiClassificationRequest
            {
                ProjectName = "GymWatch – Canada",
                Keywords = ["gym", "fitness"],
                Countries = ["Canada"],
                Industries = ["Fitness"],
                CompanyName = "Northside Strength Gym",
                Country = "Canada",
                City = "Toronto",
                Industry = "Fitness",
                CompanyDescription = "Independent gym with memberships and classes",
                Website = "https://northsidestrength.example",
                SourceUrl = "https://northsidestrength.example"
            },
            CancellationToken.None);

        Assert.True(result.IsRelevant);
        Assert.True(result.QualificationScore >= 40);
    }

    [Fact]
    public async Task Qualify_EmailOrWhatsApp_DoesNotRequireBoth()
    {
        var ai = new RuleBasedAiResearchService();
        var emailOnly = await ai.QualifyAsync(
            BuildGymRequest(email: "info@gym.example", whatsApp: null),
            CancellationToken.None);
        var whatsAppOnly = await ai.QualifyAsync(
            BuildGymRequest(email: null, whatsApp: "+14165550100"),
            CancellationToken.None);

        Assert.True(emailOnly.IsRelevant);
        Assert.True(whatsAppOnly.IsRelevant);
    }

    [Fact]
    public void DiscoveryState_TracksRejectionReasonsAndProviderDiagnostics()
    {
        var state = new ResearchDiscoveryState
        {
            ActiveProviderName = "Brave",
            SearchQueriesExecuted = 24,
            SearchResultsReceived = 240,
            SearchRequestsSuccessful = 24,
            SearchRequestsFailed = 1,
            Http429Count = 1
        };
        state.RecordRejection("Equipment supplier");
        state.RecordRejection("Equipment supplier");
        state.RecordRejection("Personal trainer only");

        Assert.Equal(2, state.RejectionReasons["Equipment supplier"]);
        Assert.Equal(1, state.RejectionReasons["Personal trainer only"]);
        Assert.Contains("Brave", state.SerializeProviderDiagnostics(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Equipment supplier", state.SerializeRejectionReasons(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HasAcceptableContact_EmailOrWhatsApp_IsValid()
    {
        Assert.True(HasContact(includeEmail: true, includeWhatsApp: true, email: "a@b.com", whatsApp: null));
        Assert.True(HasContact(includeEmail: true, includeWhatsApp: true, email: null, whatsApp: "+1"));
        Assert.False(HasContact(includeEmail: true, includeWhatsApp: true, email: null, whatsApp: null));
        Assert.True(HasContact(includeEmail: true, includeWhatsApp: false, email: "a@b.com", whatsApp: null));
        Assert.False(HasContact(includeEmail: true, includeWhatsApp: false, email: null, whatsApp: "+1"));
    }

    private static AiClassificationRequest BuildGymRequest(string? email, string? whatsApp)
    {
        return new AiClassificationRequest
        {
            ProjectName = "GymWatch – Canada",
            Keywords = ["gym"],
            Countries = ["Canada"],
            Industries = ["Fitness"],
            CompanyName = "Test Gym",
            Country = "Canada",
            Industry = "Fitness",
            Website = "https://testgym.example",
            SourceUrl = "https://testgym.example",
            Email = email,
            WhatsApp = whatsApp
        };
    }

    private static bool HasContact(bool includeEmail, bool includeWhatsApp, string? email, string? whatsApp)
    {
        if (!includeEmail && !includeWhatsApp)
        {
            return true;
        }

        if (includeEmail && !string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        if (includeWhatsApp && !string.IsNullOrWhiteSpace(whatsApp))
        {
            return true;
        }

        return false;
    }
}
