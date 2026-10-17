using Sendly.Application.Abstractions;
using Sendly.Application.Services;

namespace Sendly.Application.Tests;

public class ResearchDirectoryCatalogTests
{
    [Fact]
    public void ResolveDirectorySiteQueries_ForSaudiSchools_IncludesMadares()
    {
        var context = new ProspectSourceContext
        {
            Keywords = ["school"],
            Countries = ["Saudi Arabia"],
            Industries = ["Education"]
        };

        var queries = ResearchDirectoryCatalog.ResolveDirectorySiteQueries(context);

        Assert.Contains(queries, q => q.Contains("site:madares.sa", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => q.Contains("site:parents.madares.sa", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildSeenKey_KeepsDistinctDirectoryPaths()
    {
        var a = new Uri("https://parents.madares.sa/school-search/111");
        var b = new Uri("https://parents.madares.sa/school-search/222");

        Assert.NotEqual(
            ResearchDirectoryCatalog.BuildSeenKey(a),
            ResearchDirectoryCatalog.BuildSeenKey(b));
    }

    [Fact]
    public void DeduplicateAndMerge_DoesNotCollapseDistinctMadaresListings()
    {
        var merged = ProspectCandidateDeduplicator.DeduplicateAndMerge(
        [
            new ProspectCandidate
            {
                CompanyName = "Riyadh International School",
                SourceUrl = "https://parents.madares.sa/school-search/111",
                SourceName = "Directory (parents.madares.sa)",
                Country = "Saudi Arabia",
                City = "Riyadh"
            },
            new ProspectCandidate
            {
                CompanyName = "Jeddah Private School",
                SourceUrl = "https://parents.madares.sa/school-search/222",
                SourceName = "Directory (parents.madares.sa)",
                Country = "Saudi Arabia",
                City = "Jeddah"
            }
        ]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void Generate_ForSaudiSchools_PrioritizesMadaresSiteQueries()
    {
        var context = new ProspectSourceContext
        {
            ProjectName = "AqarPay Schools – Saudi Arabia",
            Keywords = ["school", "private school"],
            Countries = ["Saudi Arabia"],
            Industries = ["Education"],
            RequestedCount = 50
        };

        var queries = ResearchSearchQueryGenerator.Generate(context, 80).ToList();

        Assert.Contains(queries, q => q.Contains("site:madares.sa", StringComparison.OrdinalIgnoreCase));
        var madaresIndex = queries.FindIndex(q => q.Contains("site:madares.sa", StringComparison.OrdinalIgnoreCase));
        var cityIndex = queries.FindIndex(q =>
            q.Contains("Riyadh", StringComparison.OrdinalIgnoreCase)
            && !q.Contains("site:", StringComparison.OrdinalIgnoreCase));
        Assert.True(madaresIndex >= 0);
        Assert.True(cityIndex < 0 || madaresIndex < cityIndex);
    }
}
