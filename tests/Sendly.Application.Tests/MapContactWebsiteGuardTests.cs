using Sendly.Application.Services.MapResearch;

namespace Sendly.Application.Tests;

public class MapContactWebsiteGuardTests
{
    [Fact]
    public void Rejects_Junk_Websites()
    {
        Assert.False(MapContactWebsiteGuard.IsPlausibleWebsite("https://www.youtube.com", "RAK Academy"));
        Assert.False(MapContactWebsiteGuard.IsPlausibleWebsite("https://ar.wikipedia.org/wiki/x", "School"));
        Assert.False(MapContactWebsiteGuard.IsPlausibleWebsite(
            "https://rqeeqa.com/baby-names/happy-girl-names.html",
            "RAK Academy"));
        Assert.True(MapContactWebsiteGuard.IsPlausibleWebsite("https://www.csd.ae/", "Chinese School Dubai"));
    }

    [Fact]
    public void Rejects_Year_Like_Phones()
    {
        Assert.False(MapContactWebsiteGuard.IsPlausiblePhone("2015 0 4143"));
        Assert.True(MapContactWebsiteGuard.IsPlausiblePhone("+971 4 201 3027"));
        Assert.True(MapContactWebsiteGuard.IsPlausiblePhone("06 542 6323"));
    }

    [Fact]
    public void Hit_Must_Match_Business_Name()
    {
        Assert.True(MapContactWebsiteGuard.HitMatchesBusiness(
            "Chinese School Dubai",
            "Chinese School Dubai - Official Site",
            "Admissions and contact",
            "https://www.csd.ae/"));
        Assert.False(MapContactWebsiteGuard.HitMatchesBusiness(
            "Chinese School Dubai",
            "Baby names list",
            "Happy girl names",
            "https://rqeeqa.com/baby-names/happy-girl-names.html"));
    }

    [Fact]
    public void Official_Site_Works_For_Any_Business_Type()
    {
        Assert.True(MapContactWebsiteGuard.LooksLikeOfficialBusinessSite(
            "Chinese School Dubai",
            "Chinese School Dubai",
            "Admissions",
            "https://www.csd.ae/"));
        Assert.True(MapContactWebsiteGuard.LooksLikeOfficialBusinessSite(
            "Anytime Fitness Dubai Marina",
            "Anytime Fitness Dubai Marina",
            "Gym membership",
            "https://www.anytimefitness.com/"));
        Assert.False(MapContactWebsiteGuard.LooksLikeOfficialBusinessSite(
            "British International School",
            "British International School UAE",
            "About chrome browser",
            "https://www.google.co.uk/chrome"));
        Assert.False(MapContactWebsiteGuard.LooksLikeOfficialBusinessSite(
            "Higher College of Technology",
            "Higher College of Technology - YouTube",
            "Watch videos",
            "https://www.youtube.com/watch?v=abc"));
    }
}
