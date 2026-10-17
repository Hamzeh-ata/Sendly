namespace Sendly.Application.Abstractions;

public class BraveSearchQuery
{
    public string Query { get; set; } = string.Empty;

    public int Count { get; set; } = 10;

    public int Offset { get; set; }

    public string? Country { get; set; }

    public string? SearchLang { get; set; }

    public string? Freshness { get; set; }

    public string? SafeSearch { get; set; }
}

public class BraveSearchResultItem
{
    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Source { get; set; } = "Brave";

    public string SearchProvider { get; set; } = "Brave";
}

public class BraveSearchResponse
{
    public List<BraveSearchResultItem> Results { get; set; } = [];

    public string Provider { get; set; } = "Brave";

    public int? TotalResults { get; set; }

    public bool HasMoreResults { get; set; }
}

public class ProviderConnectionTestResult
{
    public bool Success { get; set; }

    public string Status { get; set; } = "NotConfigured";

    public string? Message { get; set; }
}

public interface IBraveSearchClient
{
    Task<BraveSearchResponse> SearchAsync(BraveSearchQuery query, CancellationToken cancellationToken);

    Task<ProviderConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken);
}

public interface IBraveSearchSecretStore
{
    string? ReadApiKey();

    void WriteApiKey(string apiKey);

    void ClearApiKey();
}
