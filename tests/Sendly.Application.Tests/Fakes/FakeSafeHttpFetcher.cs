using Sendly.Application.Abstractions;

namespace Sendly.Application.Tests.Fakes;

public class FakeSafeHttpFetcher : ISafeHttpFetcher
{
    private readonly Dictionary<string, Func<SafeHttpFetchRequest, SafeHttpFetchResult>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public List<Uri> RequestedUrls { get; } = [];

    public void Map(string url, SafeHttpFetchResult result)
    {
        _handlers[Normalize(url)] = _ => Clone(result, url);
    }

    public void Map(string url, Func<SafeHttpFetchRequest, SafeHttpFetchResult> handler)
    {
        _handlers[Normalize(url)] = handler;
    }

    public Task<SafeHttpFetchResult> FetchAsync(
        SafeHttpFetchRequest request,
        CancellationToken cancellationToken)
    {
        RequestedUrls.Add(request.Url);
        var key = Normalize(request.Url.ToString());
        if (_handlers.TryGetValue(key, out var handler))
        {
            var result = handler(request);
            result.RequestedUrl = request.Url;
            result.FinalUrl ??= request.Url;
            return Task.FromResult(result);
        }

        return Task.FromResult(new SafeHttpFetchResult
        {
            RequestedUrl = request.Url,
            FinalUrl = request.Url,
            Succeeded = false,
            StatusCode = 404,
            Error = $"HTTP 404 (no fake mapping for {request.Url})"
        });
    }

    public static SafeHttpFetchResult Html(string url, string body, int statusCode = 200)
        => new()
        {
            RequestedUrl = new Uri(url),
            FinalUrl = new Uri(url),
            Succeeded = true,
            StatusCode = statusCode,
            ContentType = "text/html; charset=utf-8",
            Body = body,
            ContentLengthBytes = body.Length
        };

    public static SafeHttpFetchResult Forbidden(string url)
        => new()
        {
            RequestedUrl = new Uri(url),
            FinalUrl = new Uri(url),
            Succeeded = false,
            StatusCode = 403,
            Error = "HTTP 403"
        };

    public static SafeHttpFetchResult NotFound(string url)
        => new()
        {
            RequestedUrl = new Uri(url),
            FinalUrl = new Uri(url),
            Succeeded = false,
            StatusCode = 404,
            Error = "HTTP 404"
        };

    public static SafeHttpFetchResult RobotsBlocked(string url)
        => new()
        {
            RequestedUrl = new Uri(url),
            FinalUrl = new Uri(url),
            Succeeded = false,
            BlockedByRobots = true,
            Error = "Crawling is disallowed by robots.txt."
        };

    private static string Normalize(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
        }

        return url.Trim().TrimEnd('/').ToLowerInvariant();
    }

    private static SafeHttpFetchResult Clone(SafeHttpFetchResult source, string url)
        => new()
        {
            Succeeded = source.Succeeded,
            RequestedUrl = new Uri(url),
            FinalUrl = source.FinalUrl ?? new Uri(url),
            StatusCode = source.StatusCode,
            ContentType = source.ContentType,
            Body = source.Body,
            ContentLengthBytes = source.ContentLengthBytes,
            Error = source.Error,
            BlockedBySsrf = source.BlockedBySsrf,
            BlockedByRobots = source.BlockedByRobots,
            WasRedirected = source.WasRedirected,
            RedirectCount = source.RedirectCount,
            DurationMs = source.DurationMs ?? 1
        };
}
