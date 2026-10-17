namespace Sendly.Application.Abstractions;

public class SearXngSearchQuery
{
    public string Query { get; set; } = string.Empty;

    public int Count { get; set; } = 10;

    public int PageNumber { get; set; } = 1;

    public string? Language { get; set; }
}

public class SearXngSearchResultItem
{
    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Content { get; set; }

    public string? Engine { get; set; }

    public string SearchProvider { get; set; } = "SearXNG";
}

public class SearXngSearchResponse
{
    public List<SearXngSearchResultItem> Results { get; set; } = [];

    public string Provider { get; set; } = "SearXNG";

    public int? TotalResults { get; set; }
}

public interface ISearXngSearchClient
{
    Task<SearXngSearchResponse> SearchAsync(SearXngSearchQuery query, CancellationToken cancellationToken);

    Task<ProviderConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken);
}
