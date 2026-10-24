using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public class WebSearchRequest
{
    public string Query { get; set; } = string.Empty;

    public int MaxResults { get; set; } = 10;

    public int Offset { get; set; }

    public string? Country { get; set; }

    public string? Language { get; set; }

    public List<string> Keywords { get; set; } = [];

    public List<string> Countries { get; set; } = [];
}

public class WebSearchHit
{
    public string Url { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Snippet { get; set; }

    public string? Provider { get; set; }

    public int Score { get; set; }
}

public class WebSearchOutcome
{
    public string Provider { get; set; } = string.Empty;

    public SearchProviderHealthState State { get; set; } = SearchProviderHealthState.NoResults;

    public IReadOnlyList<WebSearchHit> Hits { get; set; } = [];

    public int ProviderResultCount { get; set; }

    public int AcceptedCount { get; set; }

    public string? ErrorSummary { get; set; }

    public List<string> UnresponsiveEngines { get; set; } = [];

    public bool IsTransient { get; set; }

    public static WebSearchOutcome FromHits(string provider, IReadOnlyList<WebSearchHit> hits)
    {
        var list = hits.Where(x => !string.IsNullOrWhiteSpace(x.Url)).ToList();
        return new WebSearchOutcome
        {
            Provider = provider,
            State = list.Count == 0
                ? SearchProviderHealthState.NoResults
                : SearchProviderHealthState.Healthy,
            Hits = list,
            ProviderResultCount = list.Count,
            AcceptedCount = list.Count
        };
    }

    public static WebSearchOutcome Unavailable(string provider, string? summary, bool transient = false)
        => new()
        {
            Provider = provider,
            State = SearchProviderHealthState.Unavailable,
            ErrorSummary = summary,
            IsTransient = transient
        };

    public static WebSearchOutcome Invalid(string provider, string? summary)
        => new()
        {
            Provider = provider,
            State = SearchProviderHealthState.InvalidResponse,
            ErrorSummary = summary
        };
}

public interface IWebSearchProvider
{
    string Name { get; }

    bool IsEnabled { get; }

    Task<IReadOnlyList<WebSearchHit>> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken);
}

public interface IDiagnosticWebSearchProvider : IWebSearchProvider
{
    Task<WebSearchOutcome> SearchDetailedAsync(WebSearchRequest request, CancellationToken cancellationToken);

    Task<WebSearchOutcome> HealthCheckAsync(CancellationToken cancellationToken);
}
