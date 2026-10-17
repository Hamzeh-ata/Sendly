namespace Sendly.Application.Options;

public class BraveSearchOptions
{
    public const string SectionName = "BraveSearch";

    public bool Enabled { get; set; } = true;

    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.search.brave.com/res/v1/web/search";

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxResultsPerQuery { get; set; } = 10;

    public int MaxRequestsPerResearchRun { get; set; } = 100;

    public int RequestDelayMilliseconds { get; set; } = 250;

    public int MaxRetryCount { get; set; } = 2;
}
