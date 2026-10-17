using System.Net;
using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class HtmlWebSearchProvider : IWebSearchProvider
{
    private static readonly Regex ResultLinkRegex = new(
        @"uddg=([^&""]+)|href=""/(l/\?uddg=([^&""]+)|([^""]+))""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AbsoluteHrefRegex = new(
        @"href=[""](https?://[^""]+)[""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResearchOptions _options;
    private readonly BraveSearchOptions _braveOptions;
    private readonly ILogger<HtmlWebSearchProvider> _logger;

    public HtmlWebSearchProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<ResearchOptions> options,
        IOptions<BraveSearchOptions> braveOptions,
        ILogger<HtmlWebSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _braveOptions = braveOptions.Value;
        _logger = logger;
    }

    public string Name => "HtmlSearch";

    public bool IsEnabled =>
        _options.EnableSearchProvider
        && _options.EnableHtmlSearchFallback
        && string.IsNullOrWhiteSpace(_options.BingSearchApiKey)
        && (string.IsNullOrWhiteSpace(_options.GoogleSearchApiKey)
            || string.IsNullOrWhiteSpace(_options.GoogleSearchEngineId))
        && !(_braveOptions.Enabled && !string.IsNullOrWhiteSpace(_braveOptions.ApiKey));

    public async Task<IReadOnlyList<WebSearchHit>> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        try
        {
            var client = _httpClientFactory.CreateClient("HtmlSearch");
            var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(request.Query)}";
            using var response = await client.GetAsync(url, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTML search failed with status {Status}", (int)response.StatusCode);
                return [];
            }

            return ParseHtml(html, request.MaxResults);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "HTML search failed");
            return [];
        }
    }

    private static List<WebSearchHit> ParseHtml(string html, int maxResults)
    {
        var result = new List<WebSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AbsoluteHrefRegex.Matches(html))
        {
            var raw = WebUtility.HtmlDecode(match.Groups[1].Value);
            if (!TryNormalizeExternalUrl(raw, out var url))
            {
                continue;
            }

            if (!seen.Add(url))
            {
                continue;
            }

            result.Add(new WebSearchHit { Url = url });
            if (result.Count >= maxResults)
            {
                break;
            }
        }

        if (result.Count > 0)
        {
            return result;
        }

        foreach (Match match in ResultLinkRegex.Matches(html))
        {
            var encoded = match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Groups[3].Success
                    ? match.Groups[3].Value
                    : match.Groups[4].Value;
            if (string.IsNullOrWhiteSpace(encoded))
            {
                continue;
            }

            var decoded = WebUtility.UrlDecode(encoded);
            if (!TryNormalizeExternalUrl(decoded, out var url) || !seen.Add(url))
            {
                continue;
            }

            result.Add(new WebSearchHit { Url = url });
            if (result.Count >= maxResults)
            {
                break;
            }
        }

        return result;
    }

    private static bool TryNormalizeExternalUrl(string? raw, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.Host.Contains("duckduckgo", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        url = uri.GetLeftPart(UriPartial.Path);
        return true;
    }
}
