using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class OpenStreetMapDiscoverySource : IUrlDiscoverySource, IDiscoverySourceAdapter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResearchOptions _options;
    private readonly IChallengedSourceRegistry _challenges;
    private readonly ILogger<OpenStreetMapDiscoverySource> _logger;
    private DateTimeOffset? _lastSuccessAtUtc;
    private int _usefulYield;
    private string? _lastFailureSummary;

    public OpenStreetMapDiscoverySource(
        IHttpClientFactory httpClientFactory,
        IOptions<ResearchOptions> options,
        IChallengedSourceRegistry challenges,
        ILogger<OpenStreetMapDiscoverySource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _challenges = challenges;
        _logger = logger;
    }

    public string Name => "OpenStreetMapNominatim";

    public DiscoverySourceCapabilities Capabilities { get; } = new()
    {
        Name = "OpenStreetMapNominatim",
        SourceType = DiscoverySourceType.OpenStreetMap,
        SupportsIndustryFilter = true,
        SupportsCountryFilter = true,
        RequiresPaidApi = false,
        RequiresHumanVerification = false,
        AccessNotes =
            "Uses Nominatim (nominatim.openstreetmap.org) under OSMF usage policy: identify app via User-Agent, max ~1 req/sec, no heavy bulk geocoding. Returns OSM object websites when present.",
        SupportedRegions = ["global"]
    };

    public bool IsEnabled(UrlDiscoveryContext context)
        => _options.EnableOpenStreetMapDiscovery
           && context.Countries.Count > 0
           && context.Industries.Count > 0
           && !_challenges.IsTemporarilyDisabled(Name, DateTimeOffset.UtcNow);

    public DiscoverySourceHealthReport GetHealth(DateTimeOffset nowUtc)
        => new()
        {
            Name = Name,
            Health = _challenges.IsTemporarilyDisabled(Name, nowUtc)
                ? SearchProviderHealthState.Unavailable
                : _lastSuccessAtUtc is null
                    ? SearchProviderHealthState.NoResults
                    : SearchProviderHealthState.Healthy,
            LastSuccessAtUtc = _lastSuccessAtUtc,
            LastFailureSummary = _lastFailureSummary,
            IsTemporarilyDisabled = _challenges.IsTemporarilyDisabled(Name, nowUtc),
            UsefulResultYield = _usefulYield,
            AccessRestrictions = ["OSMF Nominatim usage policy", "1 request/sec soft limit"]
        };

    public async Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var queries = BuildQueries(context);
        if (queries.Count == 0)
        {
            return [];
        }

        var client = _httpClientFactory.CreateClient("OpenStreetMap");
        var result = new List<DiscoveredUrlCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limit = Math.Clamp(context.MaxUrls / 5, 5, 20);

        var query = queries[0];
        try
        {
            var url =
                $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=jsonv2&addressdetails=0&extratags=1&limit=8";
            using var response = await client.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var classification = HttpAccessResponseClassifier.Classify(response.StatusCode, body);
            if (classification.ShouldDisableSource
                || classification.IsChallenge
                || classification.Kind == HttpAccessResponseKind.RateLimited)
            {
                _challenges.RecordChallenge(
                    Name,
                    classification.Kind,
                    classification.Summary,
                    DateTimeOffset.UtcNow,
                    (int)response.StatusCode);
                _lastFailureSummary = classification.Summary;
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                _lastFailureSummary = $"Nominatim HTTP {(int)response.StatusCode}";
                _logger.LogWarning("Nominatim returned {Status} for {Query}", (int)response.StatusCode, query);
                return [];
            }

            foreach (var candidate in ParseResults(body, context, seen))
            {
                result.Add(candidate);
                if (result.Count >= limit)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _lastFailureSummary = ex.Message;
            _logger.LogWarning(ex, "OpenStreetMap discovery failed for {Query}", query);
        }

        if (result.Count > 0)
        {
            _lastSuccessAtUtc = DateTimeOffset.UtcNow;
            _usefulYield += result.Count;
            _challenges.RecordSuccess(Name);
        }

        return result;
    }

    private static List<string> BuildQueries(UrlDiscoveryContext context)
    {
        var queries = new List<string>();
        var country = context.Countries.FirstOrDefault() ?? string.Empty;
        foreach (var industry in context.Industries.Take(4))
        {
            if (string.IsNullOrWhiteSpace(industry))
            {
                continue;
            }

            queries.Add($"{industry} {country}".Trim());
        }

        return queries;
    }

    private static List<DiscoveredUrlCandidate> ParseResults(
        string body,
        UrlDiscoveryContext context,
        HashSet<string> seen)
    {
        var result = new List<DiscoveredUrlCandidate>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var website = ReadWebsite(item);
                if (string.IsNullOrWhiteSpace(website))
                {
                    continue;
                }

                var canonical = UrlCanonicalizer.Canonicalize(website);
                if (string.IsNullOrWhiteSpace(canonical) || !seen.Add(canonical))
                {
                    continue;
                }

                var host = UrlCanonicalizer.ExtractDomain(canonical);
                if (!CountryScopeHelper.HostMatchesCountries(host, context.Countries)
                    && !string.IsNullOrWhiteSpace(host)
                    && context.Countries.Count > 0)
                {
                    continue;
                }

                var name = item.TryGetProperty("display_name", out var display)
                    ? display.GetString()
                    : null;
                result.Add(new DiscoveredUrlCandidate
                {
                    Url = canonical,
                    SourceType = DiscoverySourceType.OpenStreetMap,
                    SourceName = "OpenStreetMapNominatim",
                    Depth = 0,
                    LinkText = name,
                    IsIndustryRelevant = true,
                    DiscoveryReasonCode = "discovery.openstreetmap",
                    PriorityBoost = 18
                });
            }
        }
        catch (JsonException)
        {
            return result;
        }

        return result;
    }

    private static string? ReadWebsite(JsonElement item)
    {
        if (!item.TryGetProperty("extratags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (tags.TryGetProperty("website", out var website))
        {
            return website.GetString();
        }

        if (tags.TryGetProperty("contact:website", out var contactWebsite))
        {
            return contactWebsite.GetString();
        }

        if (tags.TryGetProperty("url", out var url))
        {
            return url.GetString();
        }

        return null;
    }
}
