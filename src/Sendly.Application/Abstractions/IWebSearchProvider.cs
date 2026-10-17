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

public interface IWebSearchProvider
{
    string Name { get; }

    bool IsEnabled { get; }

    Task<IReadOnlyList<WebSearchHit>> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken);
}
