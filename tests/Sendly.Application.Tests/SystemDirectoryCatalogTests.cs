using Sendly.Application.Services;

namespace Sendly.Application.Tests;

public class SystemDirectoryCatalogTests
{
    [Fact]
    public void Resolve_ForSaudiCleaning_ReturnsScopedDirectories()
    {
        var entries = SystemDirectoryCatalog.Resolve(
            ["Saudi Arabia"],
            ["Commercial cleaning"]);

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.BaseUrl)));
        Assert.Contains(entries, e =>
            e.Countries.Any(c => c.Contains("Saudi", StringComparison.OrdinalIgnoreCase))
            || e.Industries.Count == 0);
    }

    [Fact]
    public void Resolve_UnmatchedIndustry_DoesNotFallBackToCountryOnly()
    {
        var entries = SystemDirectoryCatalog.Resolve(["Saudi Arabia"], ["UnmatchedIndustryXYZ"]);

        Assert.Empty(entries);
    }

    [Fact]
    public void Resolve_UsPrivateSchools_ExcludesCleaningAssociations()
    {
        var entries = SystemDirectoryCatalog.Resolve(
            ["United States"],
            ["Private School", "Independent School"]);

        Assert.DoesNotContain(entries, e =>
            e.BaseUrl.Contains("issa.com", StringComparison.OrdinalIgnoreCase)
            || e.BaseUrl.Contains("bscai.org", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, e =>
            e.BaseUrl.Contains("theinternationalschools.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, e =>
            e.BaseUrl.Contains("opencorporates.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, e =>
            e.BaseUrl.Contains("nais.org", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, e =>
            e.BaseUrl.Contains("privateschoolreview.com", StringComparison.OrdinalIgnoreCase));
    }
}
