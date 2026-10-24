using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Application.Services.MapResearch;
using Sendly.Domain.Entities;

namespace Sendly.Application.Tests;

public class MapResearchPlannerTests
{
    [Fact]
    public async Task BuildPlan_GymsInJordan_MapsFitnessTags_NotCleaning()
    {
        var project = BuildProject(
            "Jordan Gyms",
            ["Gyms", "Fitness centers"],
            ["Jordan"],
            ["gym", "fitness"],
            ["school", "cleaning"]);
        var plan = await BuildPlanAsync(project);

        Assert.True(plan.IsValid);
        Assert.Contains(plan.KeywordFamilies, x => x.BusinessConcept.Contains("Gym", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.SourceCategoryMappings, x =>
            x.IsSupported && x.SourceCategory.Contains("fitness", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.SourceCategoryMappings, x =>
            x.IsSupported && x.SourceCategory.Contains("cleaning", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.GeographicCells, x =>
            string.Equals(x.CountryCode, "JO", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPlan_CleaningInUae_MapsCleaningTags_NotGym()
    {
        var project = BuildProject(
            "UAE Cleaning",
            ["Commercial cleaning"],
            ["United Arab Emirates"],
            ["cleaning", "janitorial"],
            ["gym", "school"]);
        var plan = await BuildPlanAsync(project);

        Assert.True(plan.IsValid);
        Assert.Contains(plan.SourceCategoryMappings, x =>
            x.IsSupported && x.SourceCategory.Contains("clean", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.SourceCategoryMappings, x =>
            x.IsSupported && (x.SourceCategory.Contains("fitness", StringComparison.OrdinalIgnoreCase)
                              || x.SourceCategory.Contains("gym", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(plan.GeographicCells, x =>
            string.Equals(x.CountryCode, "AE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPlan_EscapeRoomsInSaudi_MapsEscapeGameTag()
    {
        var project = BuildProject(
            "KSA Escape Rooms",
            ["Escape rooms", "Indoor activities"],
            ["Saudi Arabia"],
            ["escape room", "indoor activity"],
            ["hotel", "restaurant"]);
        var plan = await BuildPlanAsync(project);

        Assert.True(plan.IsValid);
        Assert.Contains(plan.SourceCategoryMappings, x =>
            x.IsSupported && x.SourceCategory.Contains("escape", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.SourceCategoryMappings, x =>
            x.IsSupported && x.SourceCategory.Contains("hotel", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.GeographicCells, x =>
            string.Equals(x.CountryCode, "SA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_Rejects_ExcludedCategoryMapping()
    {
        var plan = new MapResearchPlan
        {
            Specification = new ResearchSpecification
            {
                IsValid = true,
                ApprovedIndustries = ["Gyms"],
                TargetCountries = ["Jordan"],
                ExcludedIndustries = ["school"]
            },
            KeywordFamilies =
            [
                new MapKeywordFamily
                {
                    FamilyKey = "industry:gyms",
                    OriginatingRequirement = "Gyms",
                    BusinessConcept = "Gyms",
                    IsMandatory = true,
                    Terms = [new MapKeywordTerm { Term = "gym", Kind = KeywordTermKinds.Exact }]
                }
            ],
            SourceCategoryMappings =
            [
                new MapSourceCategoryMapping
                {
                    FamilyKey = "industry:gyms",
                    OriginatingRequirement = "Gyms",
                    SourceName = "OpenStreetMapOverpass",
                    SourceCategory = "amenity=school",
                    IsSupported = true,
                    MatchKind = SourceCategoryMatchKinds.Approximate,
                    RelevanceReason = "bad"
                }
            ],
            GeographicCells =
            [
                new MapGeographicCellPlan
                {
                    RegionLabel = "JO-1",
                    CountryCode = "JO",
                    South = 29.2,
                    West = 35.0,
                    North = 33.3,
                    East = 39.2,
                    KeywordFamilyKey = "industry:gyms",
                    SourceCategories = ["amenity=school"]
                }
            ],
            MaxOverpassRequests = 40,
            MaxRecordsPerRun = 100
        };

        MapResearchPlanValidator.Validate(plan);
        Assert.False(plan.IsValid);
        Assert.Contains(plan.ValidationIssues, x => x.Code == "plan.exclusion_violation");
    }

    [Fact]
    public void GeographicPlanner_Subdivides_And_Preserves_Family()
    {
        var parent = new MapCoverageRegion
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            RegionLabel = "AE-1",
            CountryCode = "AE",
            South = 22,
            West = 51,
            North = 26,
            East = 56,
            KeywordFamilyKey = "industry:cleaning",
            SourceCategoriesJson = "[\"office=cleaning\"]",
            AttemptCount = 1
        };

        var children = GeographicCoveragePlanner.Subdivide(parent);
        Assert.Equal(4, children.Count);
        Assert.All(children, child =>
        {
            Assert.Equal(parent.KeywordFamilyKey, child.KeywordFamilyKey);
            Assert.Equal(parent.ResearchRunId, child.ResearchRunId);
            Assert.True(child.South < child.North);
            Assert.True(child.West < child.East);
        });
    }

    [Fact]
    public void Deduplicator_Merges_Same_Osm_Object()
    {
        var left = new MapPlaceRecord
        {
            Name = "Alpha Gym",
            SourceObjectKey = "osm:node/1",
            OsmElementType = "node",
            OsmElementId = "1",
            Latitude = 31.95,
            Longitude = 35.91,
            Website = "https://alpha.example"
        };
        var right = new MapPlaceRecord
        {
            Name = "Alpha Gym Branch",
            SourceObjectKey = "osm:node/1",
            OsmElementType = "node",
            OsmElementId = "1",
            Latitude = 31.95,
            Longitude = 35.91,
            Phone = "+962700000000"
        };

        var deduped = MapPlaceDeduplicator.Deduplicate([left, right]);
        Assert.Single(deduped);
        Assert.Equal("+962700000000", deduped[0].Phone);
        Assert.Equal("https://alpha.example", deduped[0].Website);
    }

    [Fact]
    public void Qualifier_Rejects_Excluded_And_Invalid_Coordinates()
    {
        var plan = new MapResearchPlan
        {
            Specification = new ResearchSpecification
            {
                ApprovedIndustries = ["Gyms"],
                TargetCountries = ["Jordan"],
                ExcludedKeywords = ["school"]
            },
            KeywordFamilies =
            [
                new MapKeywordFamily
                {
                    FamilyKey = "industry:gyms",
                    BusinessConcept = "Gyms",
                    OriginatingRequirement = "Gyms",
                    IsMandatory = true,
                    Terms = [new MapKeywordTerm { Term = "gym" }]
                }
            ]
        };

        var excluded = MapPlaceQualifier.Evaluate(
            new MapPlaceRecord
            {
                Name = "Private School Gym Class",
                Latitude = 31.9,
                Longitude = 35.9,
                CategoryTags = "amenity=school",
                KeywordFamilyKey = "industry:gyms"
            },
            plan);
        Assert.False(excluded.Accepted);

        var invalid = MapPlaceQualifier.Evaluate(
            new MapPlaceRecord
            {
                Name = "Mystery",
                Latitude = 0,
                Longitude = 0,
                KeywordFamilyKey = "industry:gyms"
            },
            plan);
        Assert.False(invalid.Accepted);
    }

    [Fact]
    public void KeywordFamilies_Do_Not_Promote_Optional_As_Universal_Default()
    {
        var request = new MapKeywordFamilyGenerationRequest
        {
            Specification = new ResearchSpecification
            {
                ApprovedIndustries = ["Commercial cleaning"],
                TargetCountries = ["United Arab Emirates"],
                ExcludedIndustries = ["gym"]
            },
            ProjectKeywords = ["janitorial"],
            MandatoryKeywords = ["Commercial cleaning"],
            OptionalKeywords = ["facility services"]
        };

        var families = MapKeywordFamilyBuilder.BuildDeterministic(request);
        Assert.DoesNotContain(families, x =>
            x.BusinessConcept.Contains("gym", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(families, x =>
            x.BusinessConcept.Contains("cleaning", StringComparison.OrdinalIgnoreCase) && x.IsMandatory);
        Assert.Contains(families, x =>
            x.BusinessConcept.Contains("facility", StringComparison.OrdinalIgnoreCase) && !x.IsMandatory);
    }

    private static async Task<MapResearchPlan> BuildPlanAsync(OutreachProject project)
    {
        var builder = new MapResearchPlanBuilder();
        var specRequest = new ProjectUnderstandingRequest
        {
            ProjectName = project.Name,
            ApprovedIndustries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Countries = project.Countries.Select(x => x.Name).ToList(),
            QueryRequireOfficialWebsite = false,
            QueryRequirePublicBusinessEmail = false,
            Budgets = new ResearchBudgetSpec { MaxSearchRequests = 40, MaxUrlsPerRun = 100 }
        };
        var spec = ProjectUnderstandingEngine.Interpret(specRequest);
        return await builder.BuildAsync(project, spec, specRequest.Budgets!, CancellationToken.None);
    }

    private static OutreachProject BuildProject(
        string name,
        IEnumerable<string> industries,
        IEnumerable<string> countries,
        IEnumerable<string> keywords,
        IEnumerable<string> excluded)
    {
        var project = new OutreachProject { Name = name };
        foreach (var industry in industries)
        {
            project.Industries.Add(new OutreachProjectIndustry { Value = industry });
        }

        foreach (var country in countries)
        {
            project.Countries.Add(new OutreachProjectCountry { Name = country });
        }

        foreach (var keyword in keywords)
        {
            project.Keywords.Add(new OutreachProjectKeyword { Value = keyword });
        }

        foreach (var item in excluded)
        {
            project.Industries.Add(new OutreachProjectIndustry { Value = item, IsExcluded = true });
            project.Keywords.Add(new OutreachProjectKeyword { Value = item, IsExcluded = true });
        }

        return project;
    }
}
