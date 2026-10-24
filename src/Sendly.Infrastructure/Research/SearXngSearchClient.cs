using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class SearXngSearchClient : ISearXngSearchClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SearXngOptions _options;
    private readonly ILogger<SearXngSearchClient> _logger;

    public SearXngSearchClient(
        IHttpClientFactory httpClientFactory,
        IOptions<SearXngOptions> options,
        ILogger<SearXngSearchClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SearXngSearchResponse> SearchAsync(SearXngSearchQuery query, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(query.Query))
        {
            throw new InvalidOperationException("Search query is required.");
        }

        var count = Math.Clamp(query.Count, 1, Math.Clamp(_options.DefaultResultsPerQuery, 1, 50));
        var pageNumber = Math.Clamp(query.PageNumber, 1, Math.Clamp(_options.MaximumPagesPerQuery, 1, 20));
        var language = string.IsNullOrWhiteSpace(query.Language)
            ? _options.DefaultLanguage
            : query.Language.Trim();
        var url = BuildSearchUrl(query.Query.Trim(), pageNumber, language);
        var maxAttempts = Math.Clamp(_options.MaxRetryCount, 0, 5) + 1;

        SearXngSearchResponse? lastUnavailable = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await SendSearchAsync(url, count, cancellationToken);
                if (response.State == SearchProviderHealthState.Unavailable
                    && attempt < maxAttempts
                    && IsTransientUnresponsive(response))
                {
                    lastUnavailable = response;
                    _logger.LogWarning(
                        "SearXNG engines unresponsive; retry {Attempt}/{MaxAttempts}",
                        attempt,
                        maxAttempts);
                    await Task.Delay(ResolveRetryDelayMs(null), cancellationToken);
                    continue;
                }

                return response;
            }
            catch (SearXngTransientException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    "SearXNG transient failure status {Status}; retry {Attempt}/{MaxAttempts}",
                    ex.StatusCode,
                    attempt,
                    maxAttempts);
                await Task.Delay(ResolveRetryDelayMs(ex.RetryAfter), cancellationToken);
            }
        }

        if (lastUnavailable is not null)
        {
            return lastUnavailable;
        }

        throw new InvalidOperationException("SearXNG request failed after retries.");
    }

    public async Task<ProviderConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return new ProviderConnectionTestResult
            {
                Success = false,
                Status = "NotConfigured",
                Message = "SearXNG base URL is not configured."
            };
        }

        try
        {
            SearXngUrlSafety.ValidateAndNormalizeBaseUrl(_options.BaseUrl, _options.AllowPrivateNetworkHosts);
            var response = await SearchAsync(
                new SearXngSearchQuery
                {
                    Query = "test search",
                    Count = 1,
                    PageNumber = 1,
                    Language = _options.DefaultLanguage
                },
                cancellationToken);

            return MapConnectionResult(response);
        }
        catch (InvalidOperationException ex)
        {
            return new ProviderConnectionTestResult
            {
                Success = false,
                Status = ResolveFailureStatus(ex.Message),
                Message = SanitizeMessage(ex.Message)
            };
        }
        catch (HttpRequestException)
        {
            return new ProviderConnectionTestResult
            {
                Success = false,
                Status = "ConnectionFailed",
                Message = "Unable to connect to SearXNG."
            };
        }
        catch (TaskCanceledException)
        {
            return new ProviderConnectionTestResult
            {
                Success = false,
                Status = "ConnectionFailed",
                Message = "SearXNG request timed out."
            };
        }
        catch (Exception)
        {
            return new ProviderConnectionTestResult
            {
                Success = false,
                Status = "ConnectionFailed",
                Message = "SearXNG connection failed."
            };
        }
    }

    private async Task<SearXngSearchResponse> SendSearchAsync(
        string url,
        int count,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("SearXng");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.RequestTimeoutSeconds, 5, 120)));

        var currentUrl = url;
        for (var redirect = 0; redirect <= 3; redirect++)
        {
            var destination = new Uri(currentUrl, UriKind.Absolute);
            await SearXngUrlSafety.EnsureSafeDestinationAsync(
                destination,
                _options.AllowPrivateNetworkHosts,
                timeoutCts.Token);

            using var request = new HttpRequestMessage(HttpMethod.Get, destination);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            ApplyOptionalBasicAuth(request);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            var status = (int)response.StatusCode;

            if (IsRedirect(response.StatusCode))
            {
                if (response.Headers.Location is null)
                {
                    throw new InvalidOperationException("SearXNG returned a redirect without a destination.");
                }

                currentUrl = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location.ToString()
                    : new Uri(destination, response.Headers.Location).ToString();
                continue;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return HandleResponse(status, body, count, contentType);
        }

        throw new InvalidOperationException("SearXNG redirect limit exceeded.");
    }

    private SearXngSearchResponse HandleResponse(int status, string body, int count, string? contentType)
    {
        if (status is 401 or 403)
        {
            _logger.LogWarning("SearXNG authorization failed with status {Status}", status);
            throw new InvalidOperationException("SearXNG authorization failed.");
        }

        if (status == 429 || status is 500 or 502 or 503 or 504)
        {
            throw new SearXngTransientException(status, null);
        }

        if (status == 400)
        {
            _logger.LogWarning("SearXNG rejected the request with status {Status}", status);
            throw new InvalidOperationException("Invalid SearXNG request.");
        }

        if (status < 200 || status >= 300)
        {
            _logger.LogWarning("SearXNG failed with status {Status}", status);
            throw new InvalidOperationException("SearXNG service failure.");
        }

        if (LooksLikeHtml(contentType, body))
        {
            return new SearXngSearchResponse
            {
                State = SearchProviderHealthState.InvalidResponse,
                ErrorSummary = "SearXNG returned HTML instead of JSON search results."
            };
        }

        return ParseResponse(body, count);
    }

    private string BuildSearchUrl(string query, int pageNumber, string? language)
    {
        var baseUri = SearXngUrlSafety.ValidateAndNormalizeBaseUrl(
            _options.BaseUrl,
            _options.AllowPrivateNetworkHosts);
        var searchUri = new Uri(baseUri, "search");
        var parameters = new List<string>
        {
            $"q={Uri.EscapeDataString(query)}",
            "format=json",
            $"pageno={pageNumber}"
        };

        if (!string.IsNullOrWhiteSpace(language))
        {
            parameters.Add($"language={Uri.EscapeDataString(language.Trim())}");
        }

        var builder = new UriBuilder(searchUri)
        {
            Query = string.Join('&', parameters)
        };
        return builder.Uri.ToString();
    }

    private static SearXngSearchResponse ParseResponse(string body, int count)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new SearXngSearchResponse
            {
                State = SearchProviderHealthState.InvalidResponse,
                ErrorSummary = "SearXNG returned an empty response."
            };
        }

        if (LooksLikeHtml(null, body) || LooksLikeChallenge(body))
        {
            return new SearXngSearchResponse
            {
                State = SearchProviderHealthState.InvalidResponse,
                ErrorSummary = LooksLikeChallenge(body)
                    ? "SearXNG returned a challenge or block page."
                    : "SearXNG returned HTML instead of JSON search results."
            };
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return new SearXngSearchResponse
            {
                State = SearchProviderHealthState.InvalidResponse,
                ErrorSummary = "SearXNG returned invalid JSON."
            };
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return new SearXngSearchResponse
                {
                    State = SearchProviderHealthState.InvalidResponse,
                    ErrorSummary = "SearXNG response is missing a usable results array.",
                    UnresponsiveEngines = ReadUnresponsiveEngines(root)
                };
            }

            var response = new SearXngSearchResponse
            {
                Provider = "SearXNG",
                UnresponsiveEngines = ReadUnresponsiveEngines(root)
            };

            foreach (var item in results.EnumerateArray())
            {
                if (response.Results.Count >= count)
                {
                    break;
                }

                var url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                response.Results.Add(new SearXngSearchResultItem
                {
                    Url = url,
                    Title = item.TryGetProperty("title", out var titleProp)
                        ? titleProp.GetString() ?? string.Empty
                        : string.Empty,
                    Content = item.TryGetProperty("content", out var contentProp)
                        ? contentProp.GetString()
                        : null,
                    Engine = item.TryGetProperty("engine", out var engineProp)
                        ? engineProp.GetString()
                        : null,
                    SearchProvider = "SearXNG"
                });
            }

            response.ProviderResultCount = response.Results.Count;
            response.TotalResults = response.Results.Count;
            ClassifyParsedResponse(response);
            return response;
        }
    }

    private static void ClassifyParsedResponse(SearXngSearchResponse response)
    {
        if (response.Results.Count > 0)
        {
            response.State = response.UnresponsiveEngines.Count > 0
                ? SearchProviderHealthState.Degraded
                : SearchProviderHealthState.Healthy;
            return;
        }

        if (response.UnresponsiveEngines.Count > 0)
        {
            response.State = SearchProviderHealthState.Unavailable;
            response.ErrorSummary = SummarizeUnresponsive(response.UnresponsiveEngines);
            return;
        }

        response.State = SearchProviderHealthState.NoResults;
    }

    private static List<SearXngUnresponsiveEngine> ReadUnresponsiveEngines(JsonElement root)
    {
        var result = new List<SearXngUnresponsiveEngine>();
        if (!root.TryGetProperty("unresponsive_engines", out var engines)
            || engines.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in engines.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var parts = item.EnumerateArray().ToList();
            var engine = parts.Count > 0 ? parts[0].GetString() : null;
            if (string.IsNullOrWhiteSpace(engine))
            {
                continue;
            }

            result.Add(new SearXngUnresponsiveEngine
            {
                Engine = engine,
                Reason = parts.Count > 1 ? parts[1].GetString() ?? string.Empty : string.Empty
            });
        }

        return result;
    }

    private static ProviderConnectionTestResult MapConnectionResult(SearXngSearchResponse response)
    {
        if (response.State is SearchProviderHealthState.Healthy or SearchProviderHealthState.Degraded)
        {
            return new ProviderConnectionTestResult
            {
                Success = true,
                Status = "ConnectionSuccessful",
                Message = response.State == SearchProviderHealthState.Degraded
                    ? "Connected with degraded search engines."
                    : "Connected successfully.",
                HealthState = response.State.ToString(),
                ResultCount = response.Results.Count,
                UnresponsiveEngines = response.UnresponsiveEngines.Select(x => $"{x.Engine}: {x.Reason}").ToList()
            };
        }

        if (response.State == SearchProviderHealthState.NoResults)
        {
            return new ProviderConnectionTestResult
            {
                Success = true,
                Status = "ConnectionSuccessful",
                Message = "Connected successfully. No results for the test query.",
                HealthState = response.State.ToString(),
                ResultCount = 0
            };
        }

        return new ProviderConnectionTestResult
        {
            Success = false,
            Status = response.State == SearchProviderHealthState.InvalidResponse
                ? "InvalidResponse"
                : "Unavailable",
            Message = response.ErrorSummary ?? "SearXNG search engines are unavailable.",
            HealthState = response.State.ToString(),
            ResultCount = response.Results.Count,
            UnresponsiveEngines = response.UnresponsiveEngines.Select(x => $"{x.Engine}: {x.Reason}").ToList()
        };
    }

    private static string SummarizeUnresponsive(List<SearXngUnresponsiveEngine> engines)
    {
        var parts = engines
            .Select(x => string.IsNullOrWhiteSpace(x.Reason) ? x.Engine : $"{x.Engine} ({x.Reason})")
            .Take(6);
        return "SearXNG engines unresponsive: " + string.Join("; ", parts);
    }

    private static bool LooksLikeHtml(string? contentType, string body)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = body.TrimStart();
        return trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeChallenge(string body)
    {
        return body.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase)
               || body.Contains("captcha", StringComparison.OrdinalIgnoreCase)
               || body.Contains("Enable JavaScript", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientUnresponsive(SearXngSearchResponse response)
    {
        if (response.UnresponsiveEngines.Count == 0)
        {
            return false;
        }

        foreach (var engine in response.UnresponsiveEngines)
        {
            var reason = engine.Reason ?? string.Empty;
            if (reason.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("suspended", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("429", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyOptionalBasicAuth(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.BasicAuthUsername))
        {
            return;
        }

        var user = _options.BasicAuthUsername.Trim();
        var password = _options.BasicAuthPassword ?? string.Empty;
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("SearXNG base URL is not configured.");
        }

        SearXngUrlSafety.ValidateAndNormalizeBaseUrl(_options.BaseUrl, _options.AllowPrivateNetworkHosts);
    }

    private int ResolveRetryDelayMs(TimeSpan? retryAfter)
    {
        if (retryAfter is not null)
        {
            return (int)Math.Clamp(retryAfter.Value.TotalMilliseconds, 250, 10000);
        }

        return Math.Clamp(_options.RequestDelayMilliseconds, 100, 5000);
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 301 or 302 or 303 or 307 or 308;
    }

    private static string ResolveFailureStatus(string message)
    {
        if (message.Contains("not configured", StringComparison.OrdinalIgnoreCase)
            || message.Contains("required", StringComparison.OrdinalIgnoreCase))
        {
            return "NotConfigured";
        }

        return "ConnectionFailed";
    }

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "SearXNG connection failed.";
        }

        return message
            .Replace("BasicAuthPassword", "password", StringComparison.OrdinalIgnoreCase)
            .Replace("Authorization", "auth", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SearXngTransientException : Exception
    {
        public SearXngTransientException(int statusCode, TimeSpan? retryAfter)
        {
            StatusCode = statusCode;
            RetryAfter = retryAfter;
        }

        public int StatusCode { get; }

        public TimeSpan? RetryAfter { get; }
    }
}
