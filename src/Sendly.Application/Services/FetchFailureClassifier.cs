using Sendly.Application.Abstractions;

namespace Sendly.Application.Services;

public enum FetchFailureKind
{
    None = 0,
    Transient = 1,
    RateLimited = 2,
    Forbidden = 3,
    RobotsDisallowed = 4,
    Unavailable = 5,
    SsrfBlocked = 6,
    UnsupportedContent = 7,
    Timeout = 8,
    RedirectLoop = 9
}

public class FetchFailureClassification
{
    public FetchFailureKind Kind { get; set; }

    public string ReasonCode { get; set; } = "fetch.ok";

    public bool RetryAppropriate { get; set; }

    public bool HostShouldBeAvoided { get; set; }

    public string Explanation { get; set; } = string.Empty;
}

public static class FetchFailureClassifier
{
    public static FetchFailureClassification Classify(SafeHttpFetchResult result)
    {
        if (result.Succeeded)
        {
            return new FetchFailureClassification
            {
                Kind = FetchFailureKind.None,
                ReasonCode = "fetch.ok",
                Explanation = "Fetch succeeded."
            };
        }

        if (result.BlockedByRobots)
        {
            return new FetchFailureClassification
            {
                Kind = FetchFailureKind.RobotsDisallowed,
                ReasonCode = "fetch.robots_disallowed",
                RetryAppropriate = false,
                HostShouldBeAvoided = true,
                Explanation = "Crawling is disallowed by robots.txt."
            };
        }

        if (result.BlockedBySsrf)
        {
            return new FetchFailureClassification
            {
                Kind = FetchFailureKind.SsrfBlocked,
                ReasonCode = "fetch.ssrf_blocked",
                RetryAppropriate = false,
                HostShouldBeAvoided = true,
                Explanation = "Destination blocked by SSRF protection."
            };
        }

        var status = result.StatusCode ?? 0;
        var error = result.Error ?? string.Empty;

        if (status == 429
            || error.Contains("429", StringComparison.OrdinalIgnoreCase)
            || error.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
        {
            return new FetchFailureClassification
            {
                Kind = FetchFailureKind.RateLimited,
                ReasonCode = "fetch.rate_limited",
                RetryAppropriate = true,
                HostShouldBeAvoided = true,
                Explanation = "Host rate-limited the request."
            };
        }

        if (status == 403
            || error.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase)
            || error.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
        {
            return new FetchFailureClassification
            {
                Kind = FetchFailureKind.Forbidden,
                ReasonCode = "fetch.forbidden",
                RetryAppropriate = false,
                HostShouldBeAvoided = true,
                Explanation = "Host returned HTTP 403 Forbidden."
            };
        }

        if (status == 401)
        {
            return new FetchFailureClassification
            {
                Kind = FetchFailureKind.Forbidden,
                ReasonCode = "fetch.unauthorized",
                RetryAppropriate = false,
                HostShouldBeAvoided = true,
                Explanation = "Host requires authentication."
            };
        }

        if (error.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || error.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return new FetchFailureClassification
            {
                Kind = FetchFailureKind.Timeout,
                ReasonCode = "fetch.timeout",
                RetryAppropriate = true,
                HostShouldBeAvoided = false,
                Explanation = "Request timed out."
            };
        }

        if (error.Contains("Unsupported content type", StringComparison.OrdinalIgnoreCase))
        {
            return new FetchFailureClassification
            {
                Kind = FetchFailureKind.UnsupportedContent,
                ReasonCode = "fetch.unsupported_content",
                RetryAppropriate = false,
                HostShouldBeAvoided = false,
                Explanation = "Response content type is not crawlable HTML."
            };
        }

        if (status is >= 500 and <= 599 || error.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase))
        {
            return new FetchFailureClassification
            {
                Kind = FetchFailureKind.Transient,
                ReasonCode = "fetch.server_error",
                RetryAppropriate = true,
                HostShouldBeAvoided = false,
                Explanation = "Host returned a server error."
            };
        }

        if (status is >= 400 and <= 499)
        {
            return new FetchFailureClassification
            {
                Kind = FetchFailureKind.Unavailable,
                ReasonCode = "fetch.client_error",
                RetryAppropriate = false,
                HostShouldBeAvoided = status is 404 or 410,
                Explanation = $"Host returned HTTP {status}."
            };
        }

        return new FetchFailureClassification
        {
            Kind = FetchFailureKind.Unavailable,
            ReasonCode = "fetch.unavailable",
            RetryAppropriate = false,
            HostShouldBeAvoided = false,
            Explanation = string.IsNullOrWhiteSpace(error) ? "Fetch failed." : error
        };
    }
}
