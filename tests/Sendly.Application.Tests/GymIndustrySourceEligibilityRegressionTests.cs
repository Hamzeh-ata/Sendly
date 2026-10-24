using Sendly.Application.Services;

namespace Sendly.Application.Tests;

public class GymIndustrySourceEligibilityRegressionTests
{
    [Fact]
    public void Us_gym_project_resolves_fitness_directories_not_cleaning()
    {
        var resolved = SystemDirectoryCatalog.Resolve(
            ["United States"],
            ["Gym", "Fitness Club", "Health Club", "Training Facility", "CrossFit"]);

        Assert.Contains(resolved, x => x.Name.Contains("Yellow Pages", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resolved, x => x.Name.Contains("IHRSA", StringComparison.OrdinalIgnoreCase)
                                       || x.Name.Contains("Club Industry", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resolved, x => x.Name.Contains("ISSA", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resolved, x => x.Name.Contains("BSCAI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            resolved,
            x => x.SourceKind.Equals("Directory", StringComparison.OrdinalIgnoreCase)
                 || x.SourceKind.Equals("Association", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Gym_industries_do_not_match_cleaning_associations()
    {
        var target = new[]
        {
            "Gym", "Fitness Club", "Health Club", "Training Facility", "CrossFit",
            "Boutique Fitness Studio", "Personal Training Center", "Fitness Center"
        };
        var issa = new[] { "Cleaning", "Commercial Cleaning", "Janitorial", "Facility", "Sanitation" };
        var bscai = new[] { "Cleaning", "Commercial Cleaning", "Building Service" };
        var openCorp = new[] { "Cleaning", "Fitness", "Gym", "Business", "Services" };

        Assert.Null(IndustrySynonymCatalog.FindIndustryOverlap(target, issa));
        Assert.Null(IndustrySynonymCatalog.FindIndustryOverlap(target, bscai));
        Assert.NotNull(IndustrySynonymCatalog.FindIndustryOverlap(target, openCorp));
    }

    [Fact]
    public void Training_facility_does_not_expand_into_facility_cleaning()
    {
        var expanded = IndustrySynonymCatalog.Expand(["Training Facility", "Gym"]);
        Assert.DoesNotContain(expanded, x => x.Contains("cleaning", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(expanded, x => x.Equals("facility management", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(expanded, x => x.Equals("Gym", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Facility_management_still_matches_cleaning_facility_sources()
    {
        var overlap = IndustrySynonymCatalog.FindIndustryOverlap(
            ["Facility Management", "Commercial Cleaning"],
            ["Cleaning", "Facility", "Sanitation"]);
        Assert.NotNull(overlap);
    }
}
