namespace Sendly.Application.Abstractions;

public class SafeHttpFetchRequest
{
    public Uri Url { get; set; } = null!;

    public int MaxRedirects { get; set; } = 5;

    public int MaxResponseBytes { get; set; } = 2_000_000;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

public class SafeHttpFetchResult
{
    public bool Succeeded { get; set; }

    public Uri RequestedUrl { get; set; } = null!;

    public Uri? FinalUrl { get; set; }

    public int? StatusCode { get; set; }

    public string? ContentType { get; set; }

    public string? Body { get; set; }

    public long ContentLengthBytes { get; set; }

    public string? Error { get; set; }

    public bool BlockedBySsrf { get; set; }

    public bool BlockedByRobots { get; set; }

    public bool WasRedirected { get; set; }

    public int RedirectCount { get; set; }

    public int? DurationMs { get; set; }
}

public interface ISafeHttpFetcher
{
    Task<SafeHttpFetchResult> FetchAsync(
        SafeHttpFetchRequest request,
        CancellationToken cancellationToken);
}
