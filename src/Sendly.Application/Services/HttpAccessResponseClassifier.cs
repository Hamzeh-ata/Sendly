using System.Net;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public class HttpAccessClassification
{
    public HttpAccessResponseKind Kind { get; set; }

    public string Summary { get; set; } = string.Empty;

    public bool IsChallenge => Kind is HttpAccessResponseKind.CaptchaChallenge
        or HttpAccessResponseKind.BotVerification
        or HttpAccessResponseKind.ConsentInterstitial;

    public bool ShouldDisableSource => IsChallenge
                                      || Kind is HttpAccessResponseKind.Forbidden
                                          or HttpAccessResponseKind.LoginRequired;

    public bool IsTransient => Kind is HttpAccessResponseKind.RateLimited
        or HttpAccessResponseKind.NetworkTimeout
        or HttpAccessResponseKind.TransientError;
}

public static class HttpAccessResponseClassifier
{
    public static HttpAccessClassification Classify(
        HttpStatusCode statusCode,
        string? body,
        bool timedOut = false,
        int parsedResultCount = -1)
    {
        if (timedOut)
        {
            return Result(HttpAccessResponseKind.NetworkTimeout, "Request timed out.");
        }

        var status = (int)statusCode;
        var html = body ?? string.Empty;

        if (status == 429)
        {
            return Result(HttpAccessResponseKind.RateLimited, "HTTP 429 rate limit.");
        }

        if (status == 403)
        {
            if (LooksLikeCaptcha(html) || LooksLikeBotVerification(html))
            {
                return Result(HttpAccessResponseKind.CaptchaChallenge, "HTTP 403 with CAPTCHA or bot challenge.");
            }

            return Result(HttpAccessResponseKind.Forbidden, "HTTP 403 forbidden.");
        }

        if (status == 401 || LooksLikeLoginRequired(html))
        {
            return Result(HttpAccessResponseKind.LoginRequired, "Login required.");
        }

        if (status == 202 || LooksLikeCaptcha(html))
        {
            return Result(HttpAccessResponseKind.CaptchaChallenge, "CAPTCHA challenge page.");
        }

        if (LooksLikeBotVerification(html))
        {
            return Result(HttpAccessResponseKind.BotVerification, "Bot or browser verification page.");
        }

        if (LooksLikeConsent(html))
        {
            return Result(HttpAccessResponseKind.ConsentInterstitial, "Consent or cookie interstitial page.");
        }

        if (status >= 500)
        {
            return Result(HttpAccessResponseKind.TransientError, $"HTTP {status} server error.");
        }

        if (status < 200 || status >= 300)
        {
            return Result(HttpAccessResponseKind.UnexpectedFormat, $"Unexpected HTTP {status}.");
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            return Result(HttpAccessResponseKind.InvalidHtml, "Empty response body.");
        }

        if (LooksLikeChallenge(html))
        {
            return Result(HttpAccessResponseKind.BotVerification, "Challenge markers in HTML body.");
        }

        if (parsedResultCount >= 0)
        {
            return parsedResultCount == 0
                ? Result(HttpAccessResponseKind.EmptyResults, "No parseable results.")
                : Result(HttpAccessResponseKind.ValidResults, $"{parsedResultCount} results.");
        }

        return Result(HttpAccessResponseKind.ValidResults, "Response accepted.");
    }

    public static bool LooksLikeChallenge(string html)
        => LooksLikeCaptcha(html) || LooksLikeBotVerification(html) || PublicSearchHtmlParser.LooksLikeChallenge(html);

    private static bool LooksLikeCaptcha(string html)
        => ContainsAny(html,
            "captcha",
            "g-recaptcha",
            "hcaptcha",
            "cf-challenge",
            "challenge-platform",
            "verify you are human",
            "are you a robot");

    private static bool LooksLikeBotVerification(string html)
        => ContainsAny(html,
            "anomaly-modal",
            "bots use",
            "unusual traffic",
            "enable javascript",
            "browser check",
            "checking your browser",
            "attention required",
            "just a moment");

    private static bool LooksLikeConsent(string html)
        => ContainsAny(html,
            "cookie consent",
            "accept all cookies",
            "consent-banner",
            "gdpr-banner",
            "we use cookies");

    private static bool LooksLikeLoginRequired(string html)
        => ContainsAny(html,
            "sign in to continue",
            "please log in",
            "login required",
            "create an account to continue");

    private static bool ContainsAny(string haystack, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HttpAccessClassification Result(HttpAccessResponseKind kind, string summary)
        => new()
        {
            Kind = kind,
            Summary = summary
        };
}
