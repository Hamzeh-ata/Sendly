using Sendly.Domain.Services;

namespace Sendly.Domain.Tests;

public class ProspectNormalizerTests
{
    [Theory]
    [InlineData("ABC Fitness", "ABC Fitness Center")]
    [InlineData("ABC Fitness", "ABC Fitness - Riyadh")]
    [InlineData("ABC Fitness Club", "ABC Fitness Center")]
    public void AreCompanyNamesLikelySame_MatchesCommonVariants(string left, string right)
    {
        Assert.True(ProspectNormalizer.AreCompanyNamesLikelySame(left, right));
    }

    [Fact]
    public void NormalizeWebsite_RemovesProtocolWwwAndTrailingSlashDifferences()
    {
        var a = ProspectNormalizer.NormalizeWebsite("https://www.abcgym.com/");
        var b = ProspectNormalizer.NormalizeWebsite("http://abcgym.com");

        Assert.Equal("abcgym.com", a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void NormalizeWebsite_KeepsSocialProfileIdentity()
    {
        var a = ProspectNormalizer.NormalizeWebsite("https://www.instagram.com/abcgym/");
        var b = ProspectNormalizer.NormalizeWebsite("http://instagram.com/abcgym");

        Assert.Equal("instagram.com/abcgym", a);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("+966 50 123 4567", "966501234567")]
    [InlineData("0501234567", "966501234567")]
    [InlineData("501234567", "966501234567")]
    [InlineData("00966501234567", "966501234567")]
    public void NormalizePhone_NormalizesSaudiFormats(string input, string expected)
    {
        Assert.Equal(expected, ProspectNormalizer.NormalizePhone(input));
    }

    [Fact]
    public void NormalizeEmail_LowercasesAndTrims()
    {
        Assert.Equal("info@abcgym.com", ProspectNormalizer.NormalizeEmail(" INFO@ABCGYM.COM "));
    }

    [Fact]
    public void ProspectSourceUrlStore_DeduplicatesNormalizedUrls()
    {
        var json = ProspectSourceUrlStore.Serialize(
        [
            "https://www.abcgym.com/",
            "http://abcgym.com",
            "https://instagram.com/abcgym"
        ]);

        var parsed = ProspectSourceUrlStore.Parse(json);
        Assert.Equal(2, parsed.Count);
    }
}
