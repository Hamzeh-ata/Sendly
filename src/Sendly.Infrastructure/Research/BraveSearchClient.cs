using System.Net;
using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class BraveSearchClient : IBraveSearchClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BraveSearchOptions _options;
    private readonly ILogger<BraveSearchClient> _logger;

    public BraveSearchClient(
        IHttpClientFactory httpClientFactory,
        IOptions<BraveSearchOptions> options,
        ILogger<BraveSearchClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BraveSearchResponse> SearchAsync(BraveSearchQuery query, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(query.Query))
        {
            throw new InvalidOperationException("Search query is required.");
        }

        var count = Math.Clamp(query.Count, 1, Math.Clamp(_options.MaxResultsPerQuery, 1, 20));
        var offset = Math.Max(0, query.Offset);
        var url = BuildUrl(query.Query.Trim(), count, offset, query);
        var maxAttempts = Math.Clamp(_options.MaxRetryCount, 0, 5) + 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await SendSearchAsync(url, cancellationToken);
            }
            catch (BraveSearchTransientException ex) when (attempt < maxAttempts)
            {
                var delayMs = ResolveRetryDelayMs(ex.RetryAfter);
                _logger.LogWarning(
                    "Brave Search transient failure status {Status}; retry {Attempt}/{MaxAttempts}",
                    ex.StatusCode,
                    attempt,
                    maxAttempts);
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        throw new InvalidOperationException("Brave Search request failed after retries.");
    }

    public async Task<ProviderConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new ProviderConnectionTestResult
            {
                Success = false,
                Status = "NotConfigured",
                Message = "Brave Search API key is not configured."
            };
        }

        try
        {
            var response = await SearchAsync(
                new BraveSearchQuery
                {
                    Query = "test search",
                    Count = 1
                },
                cancellationToken);

            return new ProviderConnectionTestResult
            {
                Success = true,
                Status = "ConnectionSuccessful",
                Message = response.Results.Count == 0
                    ? "Connected successfully. No results for the test query."
                    : "Connected successfully."
            };
        }
        catch (InvalidOperationException ex)
        {
            return new ProviderConnectionTestResult
            {
                Success = false,
                Status = "ConnectionFailed",
                Message = SanitizeMessage(ex.Message)
            };
        }
        catch (HttpRequestException)
        {
            return new ProviderConnectionTestResult
            {
                Success = false,
                Status = "ConnectionFailed",
                Message = "Unable to connect to Brave Search."
            };
        }
        catch (TaskCanceledException)
        {
            return new ProviderConnectionTestResult
            {
                Success = false,
                Status = "ConnectionFailed",
                Message = "Brave Search request timed out."
            };
        }
        catch (Exception)
        {
            return new ProviderConnectionTestResult
            {
                Success = false,
                Status = "ConnectionFailed",
                Message = "Brave Search connection failed."
            };
        }
    }

    private async Task<BraveSearchResponse> SendSearchAsync(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("BraveSearch");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Subscription-Token", _options.ApiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = (int)response.StatusCode;

        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Brave Search authorization failed with status {Status}", status);
            throw new InvalidOperationException("Invalid or unauthorized Brave Search API key.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests || IsTransientServerError(response.StatusCode))
        {
            TimeSpan? retryAfter = null;
            if (response.Headers.RetryAfter?.Delta is not null)
            {
                retryAfter = response.Headers.RetryAfter.Delta;
            }

            throw new BraveSearchTransientException(status, retryAfter);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogWarning("Brave Search rejected the request with status {Status}", status);
            throw new InvalidOperationException("Invalid Brave Search request.");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Brave Search failed with status {Status}", status);
            throw new InvalidOperationException("Brave Search service failure.");
        }

        return ParseResponse(body);
    }

    private string BuildUrl(string query, int count, int offset, BraveSearchQuery request)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.search.brave.com/res/v1/web/search"
            : _options.BaseUrl.Trim();
        var builder = new UriBuilder(baseUrl);
        var parameters = new List<string>
        {
            $"q={Uri.EscapeDataString(query)}",
            $"count={count}",
            $"offset={offset}"
        };

        AppendOptional(parameters, "country", ResolveCountry(request));
        AppendOptional(parameters, "search_lang", request.SearchLang);
        AppendOptional(parameters, "freshness", request.Freshness);
        AppendOptional(parameters, "safesearch", request.SafeSearch);

        builder.Query = string.Join('&', parameters);
        return builder.Uri.ToString();
    }

    private static string? ResolveCountry(BraveSearchQuery request)
    {
        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            return NormalizeCountryCode(request.Country);
        }

        return null;
    }

    private static string? NormalizeCountryCode(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 2)
        {
            return trimmed.ToUpperInvariant();
        }

        if (trimmed.Contains("saudi", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("السعودية", StringComparison.OrdinalIgnoreCase))
        {
            return "SA";
        }

        if (trimmed.Contains("canada", StringComparison.OrdinalIgnoreCase))
        {
            return "CA";
        }

        if (trimmed.Contains("united states", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("usa", StringComparison.OrdinalIgnoreCase))
        {
            return "US";
        }

        if (trimmed.Contains("united kingdom", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("britain", StringComparison.OrdinalIgnoreCase))
        {
            return "GB";
        }

        return null;
    }

    private static void AppendOptional(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }

    private static BraveSearchResponse ParseResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new BraveSearchResponse();
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var response = new BraveSearchResponse
        {
            Provider = "Brave"
        };

        if (root.TryGetProperty("query", out var queryNode)
            && queryNode.TryGetProperty("more_results_available", out var more)
            && more.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            response.HasMoreResults = more.GetBoolean();
        }

        if (!root.TryGetProperty("web", out var web)
            || !web.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return response;
        }

        foreach (var item in results.EnumerateArray())
        {
            var url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            response.Results.Add(new BraveSearchResultItem
            {
                Url = url,
                Title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty,
                Description = item.TryGetProperty("description", out var descriptionProp)
                    ? descriptionProp.GetString()
                    : null,
                Source = "Brave",
                SearchProvider = "Brave"
            });
        }

        response.TotalResults = response.Results.Count;
        return response;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Brave Search API key is not configured.");
        }
    }

    private int ResolveRetryDelayMs(TimeSpan? retryAfter)
    {
        if (retryAfter is not null)
        {
            var ms = (int)Math.Clamp(retryAfter.Value.TotalMilliseconds, 250, 10000);
            return ms;
        }

        return Math.Clamp(_options.RequestDelayMilliseconds, 100, 5000);
    }

    private static bool IsTransientServerError(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 500 or 502 or 503 or 504;
    }

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Brave Search connection failed.";
        }

        return message
            .Replace("X-Subscription-Token", "token", StringComparison.OrdinalIgnoreCase)
            .Replace("ApiKey", "key", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BraveSearchTransientException : Exception
    {
        public BraveSearchTransientException(int statusCode, TimeSpan? retryAfter)
        {
            StatusCode = statusCode;
            RetryAfter = retryAfter;
        }

        public int StatusCode { get; }

        public TimeSpan? RetryAfter { get; }
    }
}
