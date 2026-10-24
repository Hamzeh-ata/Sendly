using System.Net;
using System.Text;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class SafeHttpFetcher : ISafeHttpFetcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRobotsTxtService _robotsTxtService;
    private readonly ResearchOptions _options;
    private readonly ILogger<SafeHttpFetcher> _logger;

    public SafeHttpFetcher(
        IHttpClientFactory httpClientFactory,
        IRobotsTxtService robotsTxtService,
        IOptions<ResearchOptions> options,
        ILogger<SafeHttpFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _robotsTxtService = robotsTxtService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SafeHttpFetchResult> FetchAsync(
        SafeHttpFetchRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var result = new SafeHttpFetchResult
        {
            RequestedUrl = request.Url
        };

        try
        {
            return await FetchCoreAsync(request, result, cancellationToken);
        }
        finally
        {
            result.DurationMs = (int)Math.Clamp(
                (DateTimeOffset.UtcNow - started).TotalMilliseconds,
                0,
                int.MaxValue);
        }
    }

    private async Task<SafeHttpFetchResult> FetchCoreAsync(
        SafeHttpFetchRequest request,
        SafeHttpFetchResult result,
        CancellationToken cancellationToken)
    {
        if (request.Url.Scheme != Uri.UriSchemeHttp && request.Url.Scheme != Uri.UriSchemeHttps)
        {
            result.Error = "Only HTTP and HTTPS URLs are allowed.";
            result.BlockedBySsrf = true;
            return result;
        }

        if (await SsrfGuard.IsBlockedUriAsync(request.Url, cancellationToken))
        {
            result.Error = "Destination is blocked by SSRF protection.";
            result.BlockedBySsrf = true;
            return result;
        }

        var userAgent = string.IsNullOrWhiteSpace(_options.CrawlerUserAgent)
            ? "SendlyResearchBot/1.0"
            : _options.CrawlerUserAgent;

        if (!await _robotsTxtService.IsAllowedAsync(request.Url, userAgent, cancellationToken))
        {
            result.Error = "Crawling is disallowed by robots.txt.";
            result.BlockedByRobots = true;
            return result;
        }

        var client = _httpClientFactory.CreateClient("ResearchCrawler");
        var current = request.Url;
        var maxRedirects = Math.Clamp(request.MaxRedirects, 0, _options.MaxRedirects);
        var maxBytes = Math.Clamp(request.MaxResponseBytes, 16_384, _options.MaxResponseBytes);

        for (var redirect = 0; redirect <= maxRedirects; redirect++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await SsrfGuard.IsBlockedUriAsync(current, cancellationToken))
            {
                result.Error = "Redirect destination is blocked by SSRF protection.";
                result.BlockedBySsrf = true;
                return result;
            }

            if (!await _robotsTxtService.IsAllowedAsync(current, userAgent, cancellationToken))
            {
                result.Error = "Redirect destination is disallowed by robots.txt.";
                result.BlockedByRobots = true;
                return result;
            }

            using var message = new HttpRequestMessage(HttpMethod.Get, current);
            message.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            message.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(request.Timeout);

            try
            {
                using var response = await client.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);

                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        result.Error = "Redirect response missing Location header.";
                        result.StatusCode = (int)response.StatusCode;
                        return result;
                    }

                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps)
                    {
                        result.Error = "Redirect used a non-HTTP scheme.";
                        result.BlockedBySsrf = true;
                        return result;
                    }

                    current = next;
                    result.WasRedirected = true;
                    result.RedirectCount++;
                    continue;
                }

                result.StatusCode = (int)response.StatusCode;
                result.FinalUrl = current;
                result.ContentType = response.Content.Headers.ContentType?.MediaType;

                if (!response.IsSuccessStatusCode)
                {
                    result.Error = $"HTTP {(int)response.StatusCode}";
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        result.Error = "HTTP 429 Too Many Requests";
                    }

                    return result;
                }

                if (!IsAllowedContentType(result.ContentType))
                {
                    result.Error = $"Unsupported content type: {result.ContentType}";
                    return result;
                }

                var body = await ReadBodyWithLimitAsync(response.Content, maxBytes, cts.Token);
                result.Body = body;
                result.ContentLengthBytes = Encoding.UTF8.GetByteCount(body);
                result.Succeeded = true;
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                result.Error = "Request timed out.";
                return result;
            }
            catch (InvalidOperationException ex)
            {
                result.Error = ex.Message;
                return result;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Safe fetch failed for {Url}", current);
                result.Error = "HTTP request failed.";
                return result;
            }
        }

        result.Error = "Redirect limit exceeded.";
        return result;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 301 or 302 or 303 or 307 or 308;
    }

    private static bool IsAllowedContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var normalized = contentType.Trim().ToLowerInvariant();
        return normalized.Contains("text/html", StringComparison.Ordinal)
            || normalized.Contains("application/xhtml", StringComparison.Ordinal)
            || normalized.Contains("application/xml", StringComparison.Ordinal)
            || normalized.Contains("text/xml", StringComparison.Ordinal)
            || normalized.Contains("text/plain", StringComparison.Ordinal);
    }

    private static async Task<string> ReadBodyWithLimitAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException("Response exceeded size limit.");
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }
}
