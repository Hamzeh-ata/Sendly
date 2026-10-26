namespace Sendly.Application.Options;

public class OverpassOptions
{
    public const string SectionName = "Overpass";

    public string BaseUrl { get; set; } = "https://api.overpass.ch/v1/interpreter";

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 50;

    public int RequestDelayMilliseconds { get; set; } = 2000;

    public int MaxRetryCount { get; set; } = 2;
}
