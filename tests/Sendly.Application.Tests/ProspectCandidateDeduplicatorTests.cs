using Sendly.Application.Abstractions;
using Sendly.Application.Services;

namespace Sendly.Application.Tests;

public class ProspectCandidateDeduplicatorTests
{
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
