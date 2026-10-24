namespace Sendly.Application.Options;

public class SearXngOptions
{
    public const string SectionName = "SearXng";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string DefaultLanguage { get; set; } = "en";

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int DefaultResultsPerQuery { get; set; } = 10;

    public int MaximumPagesPerQuery { get; set; } = 2;

    public int MaxRequestsPerResearchRun { get; set; } = 100;

    public int MaxRetryCount { get; set; } = 1;

    public int RequestDelayMilliseconds { get; set; } = 250;

    public bool AllowPrivateNetworkHosts { get; set; }

    public string? BasicAuthUsername { get; set; }

    public string? BasicAuthPassword { get; set; }
}
