namespace Sendly.Application.Options;

public class GooglePlacesOptions
{
    public const string SectionName = "GooglePlaces";

    public bool Enabled { get; set; }

    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://places.googleapis.com/v1/places:searchText";

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxResultsPerQuery { get; set; } = 20;

    public int MaxQueriesPerRequest { get; set; } = 4;

    public int RequestDelayMilliseconds { get; set; } = 200;
}
