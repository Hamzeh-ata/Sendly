using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;

namespace Sendly.Application.Tests;

public class ProspectCandidateDeduplicatorTests
{
    [Fact]
    public void DeduplicateAndMerge_PreservesIdentityAndAttributionConfidence()
    {
        var merged = ProspectCandidateDeduplicator.DeduplicateAndMerge(
        [
            new ProspectCandidate
            {
                CompanyName = "Samha International School",
                Website = "https://samhaschools.com",
                Email = "info@samhaschools.com",
                NameConfidence = 90,
                NameReasonCode = "identity.open_graph_site_name",
                OfficialDomain = "samhaschools.com",
                ContactAttributionState = ContactAttributionState.SameDomain,
                ContactAttributionConfidence = 95,
                ContactAttributionReasonCode = "contact.same_domain",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 80,
                SourceUrl = "https://samhaschools.com/contact-us/"
            },
            new ProspectCandidate
            {
                CompanyName = "الأرقام التي نفخر بها",
                Website = "https://samhaschools.com",
                Email = "info@samhaschools.com",
                NameConfidence = 45,
                NameReasonCode = "identity.title_only",
                ContactAttributionState = ContactAttributionState.Unverified,
                ContactAttributionConfidence = 0,
                SourceUrl = "https://samhaschools.com/about-shamha/"
            }
        ]);

        Assert.Single(merged);
        Assert.Equal("Samha International School", merged[0].CompanyName);
        Assert.Equal(90, merged[0].NameConfidence);
        Assert.Equal(ContactAttributionState.SameDomain, merged[0].ContactAttributionState);
        Assert.Equal(95, merged[0].ContactAttributionConfidence);
        Assert.Equal("samhaschools.com", merged[0].OfficialDomain);
    }

    [Fact]
    public void DeduplicateAndMerge_PrefersNonGenericCompanyName()
    {
        var merged = ProspectCandidateDeduplicator.DeduplicateAndMerge(
        [
            new ProspectCandidate
            {
                CompanyName = "الوظائف الإدارية",
                Website = "https://kc.edu.sa",
                NameConfidence = 72,
                SourceUrl = "https://career.kc.edu.sa/apply/admin-staff"
            },
            new ProspectCandidate
            {
                CompanyName = "مدينة المعرفة للتعليم",
                Website = "https://kc.edu.sa",
                NameConfidence = 78,
                NameReasonCode = "identity.open_graph_site_name",
                SourceUrl = "https://kc.edu.sa"
            }
        ]);

        Assert.Single(merged);
        Assert.Equal("مدينة المعرفة للتعليم", merged[0].CompanyName);
        Assert.Equal(78, merged[0].NameConfidence);
    }

    [Fact]
    public void DeduplicateAndMerge_MergesSameBusinessContactsAndSources()
    {
        var merged = ProspectCandidateDeduplicator.DeduplicateAndMerge(
        [
            new ProspectCandidate
            {
                CompanyName = "ABC Gym",
                Website = "https://abcgym.com",
                Email = "info@abcgym.com",
                SourceUrl = "https://abcgym.com",
                SourceName = "Official Website"
            },
            new ProspectCandidate
            {
                CompanyName = "ABC Gym",
                Website = "https://www.abcgym.com",
                WhatsApp = "+966501234567",
                SourceUrl = "https://instagram.com/abcgym",
                SourceName = "Instagram"
            },
            new ProspectCandidate
            {
                CompanyName = "ABC Gym",
                Email = "INFO@ABCGYM.COM ",
                SourceUrl = "https://directory.com/abc-gym",
                SourceName = "Directory"
            }
        ]);

        Assert.Single(merged);
        Assert.Equal("info@abcgym.com", merged[0].Email);
        Assert.Equal("+966501234567", merged[0].WhatsApp);
        Assert.Contains("Instagram", merged[0].SourceName!, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            merged[0].AdditionalSourceUrls.Count >= 1
            || (merged[0].SourceName?.Contains("Directory", StringComparison.OrdinalIgnoreCase) ?? false));
    }
}
