using System.Text.Json;
using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services.MapResearch;

public class MapOsmContactBootstrapper
{
    private static readonly Regex WikipediaTagRegex = new(
        @"^(?:(?<lang>[a-z]{2,3}):)?(?<title>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ISafeHttpFetcher _fetcher;
    private readonly ILogger<MapOsmContactBootstrapper> _logger;

    public MapOsmContactBootstrapper(
        ISafeHttpFetcher fetcher,
        ILogger<MapOsmContactBootstrapper> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    public async Task BootstrapAsync(Prospect prospect, CancellationToken cancellationToken)
    {
        ApplyRawOsmTags(prospect);
        if (!string.IsNullOrWhiteSpace(prospect.Website)
            && MapContactWebsiteGuard.IsPlausibleWebsite(prospect.Website, prospect.CompanyName)
            && IsTrustedOsmWebsite(prospect))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(prospect.Website)
            && !IsTrustedOsmWebsite(prospect)
            && !MapContactWebsiteGuard.LooksLikeOfficialBusinessSite(
                prospect.CompanyName,
                prospect.CompanyName,
                null,
                prospect.Website))
        {
            prospect.Website = null;
            prospect.NormalizedWebsite = null;
        }

        var tags = ReadTags(prospect.RawOsmTagsJson);
        if (tags.Count == 0)
        {
            tags = await FetchLiveOsmTagsAsync(prospect, cancellationToken);
            if (tags.Count > 0)
            {
                prospect.RawOsmTagsJson = JsonSerializer.Serialize(tags);
                ApplyContactsFromTags(prospect, tags);
                if (!string.IsNullOrWhiteSpace(prospect.Website)
                    && MapContactWebsiteGuard.IsPlausibleWebsite(prospect.Website, prospect.CompanyName))
                {
                    return;
                }
            }
        }

        var website = await ResolveOfficialWebsiteAsync(tags, cancellationToken);
        if (string.IsNullOrWhiteSpace(website))
        {
            return;
        }

        if (!MapContactWebsiteGuard.IsPlausibleWebsite(website, prospect.CompanyName))
        {
            return;
        }

        prospect.Website = website;
        prospect.NormalizedWebsite = UrlCanonicalizer.ExtractDomain(website);
        prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static void ApplyRawOsmTags(Prospect prospect)
    {
        var tags = ReadTags(prospect.RawOsmTagsJson);
        if (tags.Count == 0)
        {
            return;
        }

        ApplyContactsFromTags(prospect, tags);
    }

    private static bool IsTrustedOsmWebsite(Prospect prospect)
    {
        if (string.IsNullOrWhiteSpace(prospect.Website))
        {
            return false;
        }

        var tags = ReadTags(prospect.RawOsmTagsJson);
        if (tags.Count == 0)
        {
            return false;
        }

        var osmWebsite = OsmTagReader.First(
            tags,
            "website",
            "contact:website",
            "url",
            "brand:website",
            "operator:website",
            "website:en",
            "contact:website:en");
        if (string.IsNullOrWhiteSpace(osmWebsite))
        {
            return false;
        }

        return string.Equals(
            NormalizeWebsiteCompare(prospect.Website),
            NormalizeWebsiteCompare(osmWebsite),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeWebsiteCompare(string website)
    {
        var value = website.Trim().TrimEnd('/');
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = value[8..];
        }
        else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            value = value[7..];
        }

        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }

        return value;
    }

    private static void ApplyContactsFromTags(Prospect prospect, Dictionary<string, string> tags)
    {
        if (string.IsNullOrWhiteSpace(prospect.Phone))
        {
            var phone = OsmTagReader.First(
                tags,
                "phone",
                "contact:phone",
                "telephone",
                "mobile",
                "contact:mobile");
            if (MapContactWebsiteGuard.IsPlausiblePhone(phone))
            {
                prospect.Phone = phone;
                prospect.NormalizedPhone = ProspectNormalizer.NormalizePhone(phone);
            }
        }

        if (string.IsNullOrWhiteSpace(prospect.Email))
        {
            var email = OsmTagReader.First(tags, "email", "contact:email");
            if (MapContactWebsiteGuard.IsPlausibleEmail(email, prospect.CompanyName, prospect.Website))
            {
                prospect.Email = email;
                prospect.NormalizedEmail = ProspectNormalizer.NormalizeEmail(email);
            }
        }

        if (string.IsNullOrWhiteSpace(prospect.Website)
            || !MapContactWebsiteGuard.IsPlausibleWebsite(prospect.Website, prospect.CompanyName))
        {
            var website = OsmTagReader.First(
                tags,
                "website",
                "contact:website",
                "url",
                "brand:website",
                "operator:website",
                "website:en",
                "contact:website:en");
            if (MapContactWebsiteGuard.IsPlausibleWebsite(website, prospect.CompanyName))
            {
                prospect.Website = website;
                prospect.NormalizedWebsite = UrlCanonicalizer.ExtractDomain(website);
            }
        }
    }

    private async Task<string?> ResolveOfficialWebsiteAsync(
        Dictionary<string, string> tags,
        CancellationToken cancellationToken)
    {
        var wikidataId = OsmTagReader.First(tags, "wikidata", "brand:wikidata", "operator:wikidata");
        if (!string.IsNullOrWhiteSpace(wikidataId))
        {
            var fromWikiData = await ResolveWikidataOfficialWebsiteAsync(wikidataId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(fromWikiData))
            {
                return fromWikiData;
            }
        }

        var wikipedia = OsmTagReader.First(tags, "wikipedia", "wikipedia:en", "brand:wikipedia", "operator:wikipedia");
        if (string.IsNullOrWhiteSpace(wikipedia))
        {
            return null;
        }

        return await ResolveWikipediaOfficialWebsiteAsync(wikipedia, cancellationToken);
    }

    private async Task<string?> ResolveWikidataOfficialWebsiteAsync(
        string wikidataId,
        CancellationToken cancellationToken)
    {
        var id = wikidataId.Trim();
        if (!id.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var url = $"https://www.wikidata.org/wiki/Special:EntityData/{id}.json";
        var fetch = await _fetcher.FetchAsync(
            new SafeHttpFetchRequest
            {
                Url = new Uri(url),
                Timeout = TimeSpan.FromSeconds(20),
                MaxResponseBytes = 1_500_000
            },
            cancellationToken);
        if (!fetch.Succeeded || string.IsNullOrWhiteSpace(fetch.Body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(fetch.Body);
            if (!doc.RootElement.TryGetProperty("entities", out var entities))
            {
                return null;
            }

            foreach (var entity in entities.EnumerateObject())
            {
                if (!entity.Value.TryGetProperty("claims", out var claims))
                {
                    continue;
                }

                if (!claims.TryGetProperty("P856", out var p856))
                {
                    continue;
                }

                foreach (var claim in p856.EnumerateArray())
                {
                    if (!claim.TryGetProperty("mainsnak", out var snak))
                    {
                        continue;
                    }

                    if (!snak.TryGetProperty("datavalue", out var dataValue))
                    {
                        continue;
                    }

                    if (!dataValue.TryGetProperty("value", out var value))
                    {
                        continue;
                    }

                    var website = value.GetString();
                    if (!string.IsNullOrWhiteSpace(website))
                    {
                        return website.Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed parsing Wikidata entity {Id}", id);
        }

        return null;
    }

    private async Task<string?> ResolveWikipediaOfficialWebsiteAsync(
        string wikipediaTag,
        CancellationToken cancellationToken)
    {
        var match = WikipediaTagRegex.Match(wikipediaTag.Trim());
        if (!match.Success)
        {
            return null;
        }

        var lang = match.Groups["lang"].Success ? match.Groups["lang"].Value : "en";
        var title = match.Groups["title"].Value.Trim().Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var apiUrl =
            $"https://www.wikidata.org/w/api.php?action=wbgetentities&sites={lang}wiki&titles={Uri.EscapeDataString(title.Replace('_', ' '))}&props=claims&format=json";
        var fetch = await _fetcher.FetchAsync(
            new SafeHttpFetchRequest
            {
                Url = new Uri(apiUrl),
                Timeout = TimeSpan.FromSeconds(20),
                MaxResponseBytes = 1_500_000
            },
            cancellationToken);
        if (!fetch.Succeeded || string.IsNullOrWhiteSpace(fetch.Body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(fetch.Body);
            if (!doc.RootElement.TryGetProperty("entities", out var entities))
            {
                return null;
            }

            foreach (var entity in entities.EnumerateObject())
            {
                if (string.Equals(entity.Name, "-1", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!entity.Value.TryGetProperty("claims", out var claims))
                {
                    continue;
                }

                if (!claims.TryGetProperty("P856", out var p856))
                {
                    continue;
                }

                foreach (var claim in p856.EnumerateArray())
                {
                    if (!claim.TryGetProperty("mainsnak", out var snak)
                        || !snak.TryGetProperty("datavalue", out var dataValue)
                        || !dataValue.TryGetProperty("value", out var value))
                    {
                        continue;
                    }

                    var website = value.GetString();
                    if (!string.IsNullOrWhiteSpace(website))
                    {
                        return website.Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed resolving Wikipedia tag {Tag}", wikipediaTag);
        }

        return null;
    }

    private async Task<Dictionary<string, string>> FetchLiveOsmTagsAsync(
        Prospect prospect,
        CancellationToken cancellationToken)
    {
        var elementType = prospect.OsmElementType;
        var elementId = prospect.OsmElementId;
        if (string.IsNullOrWhiteSpace(elementType) || string.IsNullOrWhiteSpace(elementId))
        {
            ParseSourceObjectKey(prospect.SourceObjectKey, out elementType, out elementId);
        }

        if (string.IsNullOrWhiteSpace(elementType) || string.IsNullOrWhiteSpace(elementId))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var url = $"https://api.openstreetmap.org/api/0.6/{elementType}/{elementId}.json";
        try
        {
            var fetch = await _fetcher.FetchAsync(
                new SafeHttpFetchRequest
                {
                    Url = new Uri(url),
                    Timeout = TimeSpan.FromSeconds(20),
                    MaxResponseBytes = 500_000
                },
                cancellationToken);
            if (!fetch.Succeeded || string.IsNullOrWhiteSpace(fetch.Body))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return ParseOsmApiTags(fetch.Body);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OSM element fetch failed for {Type}/{Id}", elementType, elementId);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, string> ParseOsmApiTags(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("elements", out var elements))
            {
                return result;
            }

            foreach (var element in elements.EnumerateArray())
            {
                if (!element.TryGetProperty("tags", out var tags))
                {
                    continue;
                }

                foreach (var tag in tags.EnumerateObject())
                {
                    if (!string.IsNullOrWhiteSpace(tag.Value.GetString()))
                    {
                        result[tag.Name] = tag.Value.GetString()!;
                    }
                }
            }
        }
        catch
        {
            return result;
        }

        return result;
    }

    private static void ParseSourceObjectKey(string? sourceObjectKey, out string? elementType, out string? elementId)
    {
        elementType = null;
        elementId = null;
        if (string.IsNullOrWhiteSpace(sourceObjectKey))
        {
            return;
        }

        var value = sourceObjectKey.Trim();
        if (value.StartsWith("osm:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return;
        }

        elementType = parts[0].ToLowerInvariant();
        elementId = parts[1];
    }

    private static Dictionary<string, string> ReadTags(string? rawJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()
                    : prop.Value.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result[prop.Name] = value!;
                }
            }
        }
        catch
        {
            return result;
        }

        return result;
    }
}
