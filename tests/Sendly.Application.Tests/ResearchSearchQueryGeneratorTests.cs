using Sendly.Application.Abstractions;
using Sendly.Application.Services;

namespace Sendly.Application.Tests;

public class ResearchSearchQueryGeneratorTests
{
    [Fact]
    public void Generate_ForCanadaGymProject_ProducesMultipleCityAndBusinessQueries()
    {
        var context = new ProspectSourceContext
        {
            ProjectName = "GymWatch – Canada",
            Keywords = ["gym", "fitness"],
            Countries = ["Canada"],
            Industries = ["Fitness"],
            Locations = ["Toronto", "Vancouver", "Calgary", "Edmonton", "Montreal", "Ottawa"],
            RequestedCount = 50
        };

        var queries = ResearchSearchQueryGenerator.Generate(context, 50);
        var expanded = ResearchSearchQueryGenerator.GenerateAdditional(context, queries, 50);
        var all = queries.Concat(expanded).ToList();

        Assert.True(queries.Count >= 20);
        Assert.Contains(queries, q => q.Contains("Toronto", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => q.Contains("Vancouver", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => q.Contains("gym", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => q.Contains("fitness", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, q => q.Contains("CrossFit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => q.Contains("owner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generate_ForMontreal_IncludesFrenchQueries()
    {
        var context = new ProspectSourceContext
        {
            Keywords = ["gym"],
            Countries = ["Canada"],
            Locations = ["Montreal"]
        };

        var queries = ResearchSearchQueryGenerator.Generate(context, 40);

        Assert.Contains(queries, q => q.Contains("salle de sport", StringComparison.OrdinalIgnoreCase)
                                      || q.Contains("centre de fitness", StringComparison.OrdinalIgnoreCase)
                                      || q.Contains("gymnase", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generate_RespectsMaxQueriesLimit()
    {
        var context = new ProspectSourceContext
        {
            Keywords = ["gym"],
            Countries = ["Canada"],
            Locations = ["Toronto", "Vancouver", "Calgary"]
        };

        var queries = ResearchSearchQueryGenerator.Generate(context, 5);

        Assert.Equal(5, queries.Count);
    }

    [Fact]
    public void Generate_ForExperienceOpsWithSailingSchool_DoesNotUseK12SchoolQueries()
    {
        var context = new ProspectSourceContext
        {
            ProjectName = "ExperienceOps – United States",
            Description = "Booking management for activities, classes, sessions, and rentals.",
            ResearchInstructions = "Prioritize escape rooms, archery clubs, sailing schools and dive centers.",
            Keywords = ["escape room", "archery club", "sailing school", "kayak rental", "go-kart track"],
            Countries = ["United States"],
            Industries = ["Escape Room", "Archery Club", "Sailing School", "Kayak Rental"],
            TargetRoles = ["Owner", "Founder", "Operations Manager"],
            RequestedCount = 50
        };

        var queries = ResearchSearchQueryGenerator.Generate(context, 80);

        Assert.Contains(queries, q => q.Contains("escape room", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => q.Contains("sailing school", StringComparison.OrdinalIgnoreCase)
                                      || q.Contains("kayak rental", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("private school", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("international school", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("high school", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("kindergarten", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("admission", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("madares", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generate_ForExperienceOpsClimbingGym_DoesNotExpandToGenericFitnessGyms()
    {
        var context = new ProspectSourceContext
        {
            ProjectName = "ExperienceOps – United States",
            Keywords = ["climbing gym", "escape room", "kayak rental"],
            Countries = ["United States"],
            Industries = ["Climbing Gym", "Escape Room", "Kayak Rental"],
            TargetRoles = ["Owner", "Founder"],
            RequestedCount = 40
        };

        var queries = ResearchSearchQueryGenerator.Generate(context, 60);

        Assert.Contains(queries, q => q.Contains("climbing gym", StringComparison.OrdinalIgnoreCase)
                                      || q.Contains("escape room", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => q.Contains("booking", StringComparison.OrdinalIgnoreCase)
                                      || q.Contains("reservations", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("CrossFit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("bodybuilding gym", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("private school", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generate_ForSaudiSchools_IncludesArabicAndManyCities()
    {
        var context = new ProspectSourceContext
        {
            ProjectName = "AqarPay Schools – Saudi Arabia",
            Keywords = ["school", "private school", "international school"],
            Countries = ["Saudi Arabia"],
            Industries = ["Education"],
            RequestedCount = 50
        };

        var queries = ResearchSearchQueryGenerator.Generate(context, 80);

        Assert.True(queries.Count >= 30);
        Assert.Contains(queries, q => q.Contains("Riyadh", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => q.Contains("Jeddah", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => q.Contains("school", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(queries, q => q.Contains("Educations", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => q.Contains("مدرسة", StringComparison.OrdinalIgnoreCase)
                                      || q.Contains("الرياض", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q =>
            q.Contains("Saudi Arabia", StringComparison.OrdinalIgnoreCase)
            && q.Contains("school", StringComparison.OrdinalIgnoreCase)
            && !q.Contains("Riyadh", StringComparison.OrdinalIgnoreCase)
            && !q.Contains("Jeddah", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenerateAdditional_ForSaudiSchools_KeepsProducingAcrossRounds()
    {
        var context = new ProspectSourceContext
        {
            ProjectName = "Schools Saudi",
            Keywords = ["school"],
            Countries = ["Saudi Arabia"],
            Industries = ["Education"]
        };

        var all = ResearchSearchQueryGenerator.Generate(context, 40).ToList();
        var round1 = ResearchSearchQueryGenerator.GenerateAdditional(context, all, 40, 0);
        all.AddRange(round1);
        var round2 = ResearchSearchQueryGenerator.GenerateAdditional(context, all, 40, 1);
        all.AddRange(round2);
        var round3 = ResearchSearchQueryGenerator.GenerateAdditional(context, all, 40, 2);

        Assert.True(round1.Count + round2.Count + round3.Count > 0);
        Assert.Contains(all, q => q.Contains("مدرسة", StringComparison.OrdinalIgnoreCase)
                                  || q.Contains("directory", StringComparison.OrdinalIgnoreCase)
                                  || q.Contains("international", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenerateAdditional_ProducesNewQueriesBeyondExistingWave()
    {
        var context = new ProspectSourceContext
        {
            Keywords = ["gym", "fitness"],
            Countries = ["Canada"],
            Industries = ["Fitness"],
            Locations = ["Toronto", "Vancouver"]
        };

        var firstWave = ResearchSearchQueryGenerator.Generate(context, 10);
        var extra = ResearchSearchQueryGenerator.GenerateAdditional(context, firstWave, 20);

        Assert.True(extra.Count > 0);
        Assert.DoesNotContain(extra, q => firstWave.Contains(q, StringComparer.OrdinalIgnoreCase));
        Assert.Contains(extra, q => q.Contains("directory", StringComparison.OrdinalIgnoreCase)
                                    || q.Contains("email", StringComparison.OrdinalIgnoreCase)
                                    || q.Contains("contact", StringComparison.OrdinalIgnoreCase)
                                    || q.Contains("local", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveLocations_UsesDefaultCanadianCitiesWhenNoneProvided()
    {
        var context = new ProspectSourceContext
        {
            Countries = ["Canada"]
        };

        var locations = ResearchSearchQueryGenerator.ResolveLocations(context);

        Assert.Contains(locations, x => x.Equals("Toronto", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(locations, x => x.Equals("Vancouver", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Finland", "Helsinki")]
    [InlineData("Kuwait", "Kuwait City")]
    [InlineData("Bahrain", "Manama")]
    [InlineData("United Kingdom", "London")]
    [InlineData("Australia", "Sydney")]
    [InlineData("Ireland", "Dublin")]
    [InlineData("New Zealand", "Auckland")]
    [InlineData("Sweden", "Stockholm")]
    [InlineData("Norway", "Oslo")]
    [InlineData("Denmark", "Copenhagen")]
    [InlineData("Netherlands", "Amsterdam")]
    public void ResolveLocations_UsesDefaultCitiesForAqarPayCountries(string country, string expectedCity)
    {
        var context = new ProspectSourceContext
        {
            Countries = [country]
        };

        var locations = ResearchSearchQueryGenerator.ResolveLocations(context);

        Assert.Contains(locations, x => x.Equals(expectedCity, StringComparison.OrdinalIgnoreCase));
    }
}
