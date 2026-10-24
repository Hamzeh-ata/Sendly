namespace Sendly.Application.Abstractions;

public class LocalAiHealthResult
{
    public bool IsReachable { get; set; }

    public bool ModelAvailable { get; set; }

    public string Model { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public List<string> AvailableModels { get; set; } = [];

    public string? ErrorSummary { get; set; }
}

public class AiQueryGenerationRequest
{
    public string ProjectName { get; set; } = string.Empty;

    public string? ProjectDescription { get; set; }

    public List<string> Industries { get; set; } = [];

    public List<string> IndustrySynonyms { get; set; } = [];

    public List<string> Countries { get; set; } = [];

    public List<string> Locations { get; set; } = [];

    public List<string> Keywords { get; set; } = [];

    public List<string> ExcludedIndustries { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];

    public List<string> ExistingQueries { get; set; } = [];

    public int MaxQueries { get; set; } = 12;

    public string? CoverageSummary { get; set; }

    public string? RejectionSummary { get; set; }

    public List<string> UnderCoveredIndustries { get; set; } = [];

    public List<string> UnderCoveredLocations { get; set; } = [];

    public string? HardConstraintsSummary { get; set; }
}

public interface ILocalAiHealthService
{
    Task<LocalAiHealthResult> CheckHealthAsync(CancellationToken cancellationToken);

    Task<LocalAiHealthResult> EnsureRunningAsync(CancellationToken cancellationToken);
}

public interface ILocalAiQueryPlanner
{
    Task<IReadOnlyList<string>> GenerateSearchQueriesAsync(
        AiQueryGenerationRequest request,
        CancellationToken cancellationToken);
}
