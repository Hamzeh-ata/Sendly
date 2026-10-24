using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class HtmlWebSearchProvider : IDiagnosticWebSearchProvider, IDiscoverySourceAdapter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResearchOptions _options;
    private readonly IChallengedSourceRegistry _challenges;
    private readonly ILogger<HtmlWebSearchProvider> _logger;
    private DateTimeOffset? _lastSuccessAtUtc;
    private int _usefulYield;
    private string? _lastFailureSummary;

    public HtmlWebSearchProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<ResearchOptions> options,
        IChallengedSourceRegistry challenges,
        ILogger<HtmlWebSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _challenges = challenges;
        _logger = logger;
    }

    public string Name => "HtmlSearch";

    public bool IsEnabled => _options.EnableSearchProvider && _options.EnableHtmlSearchFallback;

    public DiscoverySourceCapabilities Capabilities { get; } = new()
    {
        Name = "HtmlSearch",
        SourceType = DiscoverySourceType.WebSearch,
        SupportsIndustryFilter = true,
        SupportsCountryFilter = true,
        RequiresPaidApi = false,
        RequiresHumanVerification = true,
        AccessNotes =
            "Uses public HTML endpoints (Brave, DuckDuckGo, Bing, Mojeek). Automated access is frequently challenged; engines are disabled temporarily when blocked. No CAPTCHA solving.",
        SupportedRegions = ["global"]
    };

    public DiscoverySourceHealthReport GetHealth(DateTimeOffset nowUtc)
    {
        var disabled = _challenges.ListDisabledKeys(nowUtc);
        var htmlEnginesDisabled = disabled.Count(x => x.StartsWith("HtmlSearch:", StringComparison.OrdinalIgnoreCase));
        return new DiscoverySourceHealthReport
        {
            Name = Name,
            Health = ResolveAggregateHealth(nowUtc),
            LastSuccessAtUtc = _lastSuccessAtUtc,
            LastFailureSummary = _lastFailureSummary,
            IsTemporarilyDisabled = htmlEnginesDisabled >= 4,
            UsefulResultYield = _usefulYield,
            AccessRestrictions = disabled
                .Where(x => x.StartsWith("HtmlSearch:", StringComparison.OrdinalIgnoreCase))
                .ToList()
        };
    }

    public async Task<IReadOnlyList<WebSearchHit>> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await SearchDetailedAsync(request, cancellationToken);
        return outcome.Hits;
    }

    public Task<WebSearchOutcome> HealthCheckAsync(CancellationToken cancellationToken)
        => SearchDetailedAsync(new WebSearchRequest { Query = "local business directory", MaxResults = 1 }, cancellationToken);

    public async Task<WebSearchOutcome> SearchDetailedAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(request.Query))
        {
            return WebSearchOutcome.Unavailable(Name, "Public HTML search is not enabled.");
        }

        var now = DateTimeOffset.UtcNow;
        var engines = BuildEngines(request);
        string? lastError = null;
        var blocked = false;
        var attempted = 0;
        var skipped = 0;
        var unresponsive = new List<string>();

        foreach (var engine in engines)
        {
            var engineKey = EngineKey(engine.Name);
            if (_challenges.IsTemporarilyDisabled(engineKey, now))
            {
                skipped++;
                unresponsive.Add(engine.Name);
                continue;
            }

            attempted++;
            var outcome = await TryEngineAsync(engine, cancellationToken);
            if (outcome.Hits.Count > 0)
            {
                _challenges.RecordSuccess(engineKey);
                _lastSuccessAtUtc = DateTimeOffset.UtcNow;
                _usefulYield += outcome.Hits.Count;
                outcome.UnresponsiveEngines = unresponsive;
                return outcome;
            }

            if (outcome.State == SearchProviderHealthState.InvalidResponse)
            {
                blocked = true;
                unresponsive.Add(engine.Name);
            }

            lastError = outcome.ErrorSummary ?? lastError;
        }

        if (attempted == 0 && skipped > 0)
        {
            return new WebSearchOutcome
            {
                Provider = Name,
                State = SearchProviderHealthState.InvalidResponse,
                ErrorSummary = "All HTML search engines are temporarily disabled after challenges.",
                UnresponsiveEngines = unresponsive
            };
        }

        if (blocked)
        {
            _lastFailureSummary = lastError;
            return new WebSearchOutcome
            {
                Provider = Name,
                State = SearchProviderHealthState.InvalidResponse,
                ErrorSummary = lastError ?? "Public search engines returned a challenge or block page.",
                UnresponsiveEngines = unresponsive
            };
        }

        return lastError is null
            ? WebSearchOutcome.FromHits(Name, [])
            : WebSearchOutcome.Unavailable(Name, lastError, transient: true);
    }

    private async Task<WebSearchOutcome> TryEngineAsync(
        SearchEngineTarget engine,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("HtmlSearch");
            using var response = await client.GetAsync(engine.Url, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var classification = HttpAccessResponseClassifier.Classify(response.StatusCode, html);
            if (classification.ShouldDisableSource || classification.IsChallenge)
            {
                _challenges.RecordChallenge(
                    EngineKey(engine.Name),
                    classification.Kind,
                    $"{engine.Name}: {classification.Summary}",
                    DateTimeOffset.UtcNow,
                    (int)response.StatusCode);
                _lastFailureSummary = $"{engine.Name}: {classification.Summary}";
                _logger.LogWarning(
                    "HTML search engine {Engine} challenged ({Kind}): {Summary}",
                    engine.Name,
                    classification.Kind,
                    classification.Summary);
                return WebSearchOutcome.Invalid(Name, $"{engine.Name}: {classification.Summary}");
            }

            if (classification.Kind == HttpAccessResponseKind.RateLimited)
            {
                _challenges.RecordChallenge(
                    EngineKey(engine.Name),
                    classification.Kind,
                    $"{engine.Name}: {classification.Summary}",
                    DateTimeOffset.UtcNow,
                    429);
                return WebSearchOutcome.Unavailable(Name, $"{engine.Name}: {classification.Summary}", transient: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Public search {Engine} failed with HTTP {Status}", engine.Name, (int)response.StatusCode);
                return WebSearchOutcome.Unavailable(
                    Name,
                    $"{engine.Name} failed with HTTP {(int)response.StatusCode}.",
                    transient: classification.IsTransient);
            }

            var hits = engine.Parser(html, 50);
            foreach (var hit in hits)
            {
                hit.Provider = engine.Name;
            }

            if (hits.Count == 0)
            {
                return WebSearchOutcome.FromHits(Name, []);
            }

            return WebSearchOutcome.FromHits(Name, hits);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            _challenges.RecordChallenge(
                EngineKey(engine.Name),
                HttpAccessResponseKind.NetworkTimeout,
                $"{engine.Name} timed out.",
                DateTimeOffset.UtcNow);
            return WebSearchOutcome.Unavailable(Name, $"{engine.Name} timed out.", transient: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Public search {Engine} failed", engine.Name);
            return WebSearchOutcome.Unavailable(Name, $"{engine.Name} request failed.", transient: true);
        }
    }

    private SearchProviderHealthState ResolveAggregateHealth(DateTimeOffset nowUtc)
    {
        var disabled = _challenges.ListDisabledKeys(nowUtc)
            .Count(x => x.StartsWith("HtmlSearch:", StringComparison.OrdinalIgnoreCase));
        if (disabled >= 4)
        {
            return SearchProviderHealthState.InvalidResponse;
        }

        if (_lastSuccessAtUtc is not null)
        {
            return disabled > 0 ? SearchProviderHealthState.Degraded : SearchProviderHealthState.Healthy;
        }

        return disabled > 0 ? SearchProviderHealthState.InvalidResponse : SearchProviderHealthState.NoResults;
    }

    private static string EngineKey(string engineName) => $"HtmlSearch:{engineName}";

    private static List<SearchEngineTarget> BuildEngines(WebSearchRequest request)
    {
        var query = Uri.EscapeDataString(request.Query);
        var count = Math.Clamp(request.MaxResults, 1, 50);
        var start = Math.Max(0, request.Offset);
        return
        [
            new SearchEngineTarget(
                "BraveHtml",
                $"https://search.brave.com/search?q={query}&source=web&offset={Math.Max(0, start / Math.Max(1, count))}",
                (html, max) => PublicSearchHtmlParser.ParseBrave(html, max, "BraveHtml")),
            new SearchEngineTarget(
                "DuckDuckGoHtml",
                $"https://html.duckduckgo.com/html/?q={query}&s={start}",
                (html, max) => PublicSearchHtmlParser.ParseDuckDuckGo(html, max, "DuckDuckGoHtml")),
            new SearchEngineTarget(
                "BingHtml",
                $"https://www.bing.com/search?q={query}&count={count}&first={start + 1}",
                (html, max) => PublicSearchHtmlParser.ParseBing(html, max, "BingHtml")),
            new SearchEngineTarget(
                "MojeekHtml",
                $"https://www.mojeek.com/search?q={query}&s={start + 1}",
                (html, max) => PublicSearchHtmlParser.ParseMojeek(html, max, "MojeekHtml"))
        ];
    }

    private sealed class SearchEngineTarget
    {
        public SearchEngineTarget(string name, string url, Func<string, int, List<WebSearchHit>> parser)
        {
            Name = name;
            Url = url;
            Parser = parser;
        }

        public string Name { get; }

        public string Url { get; }

        public Func<string, int, List<WebSearchHit>> Parser { get; }
    }
}
