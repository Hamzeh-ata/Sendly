using System.Text;
using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Ai;

public class OllamaMapResearchIntelligence : IMapResearchIntelligence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaMapResearchIntelligence> _logger;

    public OllamaMapResearchIntelligence(
        IHttpClientFactory httpClientFactory,
        IOptions<OllamaOptions> options,
        ILogger<OllamaMapResearchIntelligence> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MapKeywordFamilyGenerationResult> GenerateKeywordFamiliesAsync(
        MapKeywordFamilyGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return InvalidFamilies("Ollama is disabled.");
        }

        var prompt = BuildKeywordPrompt(request);
        var raw = await CompleteJsonAsync(prompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return InvalidFamilies("Ollama returned an empty keyword-family response.");
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var parsed = TryParseFamilies(raw!);
            if (parsed.IsValid)
            {
                return parsed;
            }

            raw = await CompleteJsonAsync(
                prompt + "\nReturn ONLY valid JSON matching the schema. Previous output was invalid.",
                cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
            {
                break;
            }
        }

        return InvalidFamilies("Ollama keyword-family JSON failed validation.");
    }

    public async Task<MapSourceCategoryMappingResult> MapSourceCategoriesAsync(
        MapSourceCategoryMappingRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return InvalidMappings("Ollama is disabled.");
        }

        var prompt = BuildMappingPrompt(request);
        var raw = await CompleteJsonAsync(prompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return InvalidMappings("Ollama returned an empty category-mapping response.");
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var parsed = TryParseMappings(raw!, request);
            if (parsed.IsValid)
            {
                return parsed;
            }

            raw = await CompleteJsonAsync(
                prompt + "\nReturn ONLY valid JSON matching the schema. Previous output was invalid.",
                cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
            {
                break;
            }
        }

        return InvalidMappings("Ollama category-mapping JSON failed validation.");
    }

    private async Task<string?> CompleteJsonAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Ollama");
            var payload = new
            {
                model = _options.Model,
                stream = false,
                format = "json",
                prompt
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("api/generate", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama map intelligence HTTP {Status}", (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("response", out var responseEl)
                ? responseEl.GetString()
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ollama map intelligence request failed");
            return null;
        }
    }

    private static string BuildKeywordPrompt(MapKeywordFamilyGenerationRequest request)
    {
        var industries = string.Join(", ", request.Specification.ApprovedIndustries);
        var countries = string.Join(", ", request.Specification.TargetCountries);
        var exclusions = string.Join(", ", request.Specification.ExcludedIndustries.Concat(request.Specification.ExcludedKeywords));
        var keywords = string.Join(", ", request.ProjectKeywords);
        return
            $$"""
            You expand search terminology for geographic business research.
            Use ONLY the project's configured industries and keywords. Do not invent unrelated industries.
            Return JSON: {"families":[{"familyKey":"string","originatingRequirement":"string","businessConcept":"string","isPrimaryTarget":true,"isMandatory":true,"terms":[{"term":"string","kind":"exact|synonym|translation|broader_related|local_variant","language":"en|ar|null","searchUse":"primary_discovery|alternate_discovery","ambiguityNotes":"string|null"}],"knownExclusions":["string"],"ambiguities":["string"]}]}
            Project industries: {{industries}}
            Project keywords: {{keywords}}
            Target countries: {{countries}}
            Exclusions (never include): {{exclusions}}
            """;
    }

    private static string BuildMappingPrompt(MapSourceCategoryMappingRequest request)
    {
        var concepts = string.Join(", ", request.KeywordFamilies.Select(x => $"{x.FamilyKey}:{x.BusinessConcept}"));
        var tags = string.Join(", ", request.AvailableTags.Take(80).Select(x => x.Tag));
        var exclusions = string.Join(", ", request.Specification.ExcludedIndustries.Concat(request.Specification.ExcludedKeywords));
        return
            $$"""
            Map project business concepts to OpenStreetMap tags chosen ONLY from the provided catalog.
            Do not invent tags. If no tag fits, list the concept under unsupportedConcepts.
            Return JSON: {"mappings":[{"familyKey":"string","originatingRequirement":"string","sourceName":"OpenStreetMapOverpass","sourceCategory":"key=value","matchKind":"exact|approximate|exploratory","relevanceReason":"string","ambiguityNotes":"string|null","isSupported":true}],"unsupportedConcepts":["string"]}
            Concepts: {{concepts}}
            Allowed OSM tags: {{tags}}
            Exclusions: {{exclusions}}
            """;
    }

    private static MapKeywordFamilyGenerationResult TryParseFamilies(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(ExtractJsonObject(raw));
            if (!doc.RootElement.TryGetProperty("families", out var familiesEl)
                || familiesEl.ValueKind != JsonValueKind.Array)
            {
                return InvalidFamilies("Missing families array.");
            }

            var families = new List<MapKeywordFamily>();
            foreach (var item in familiesEl.EnumerateArray())
            {
                var family = new MapKeywordFamily
                {
                    FamilyKey = ReadString(item, "familyKey") ?? string.Empty,
                    OriginatingRequirement = ReadString(item, "originatingRequirement") ?? string.Empty,
                    BusinessConcept = ReadString(item, "businessConcept") ?? string.Empty,
                    IsPrimaryTarget = ReadBool(item, "isPrimaryTarget", true),
                    IsMandatory = ReadBool(item, "isMandatory", true),
                    KnownExclusions = ReadStringArray(item, "knownExclusions"),
                    Ambiguities = ReadStringArray(item, "ambiguities")
                };
                if (string.IsNullOrWhiteSpace(family.BusinessConcept))
                {
                    continue;
                }

                if (item.TryGetProperty("terms", out var terms) && terms.ValueKind == JsonValueKind.Array)
                {
                    foreach (var termEl in terms.EnumerateArray())
                    {
                        var term = ReadString(termEl, "term");
                        if (string.IsNullOrWhiteSpace(term))
                        {
                            continue;
                        }

                        family.Terms.Add(new MapKeywordTerm
                        {
                            Term = term,
                            Kind = ReadString(termEl, "kind") ?? KeywordTermKinds.Synonym,
                            Language = ReadString(termEl, "language"),
                            SearchUse = ReadString(termEl, "searchUse") ?? KeywordSearchUses.AlternateDiscovery,
                            AmbiguityNotes = ReadString(termEl, "ambiguityNotes")
                        });
                    }
                }

                if (family.Terms.Count == 0)
                {
                    family.Terms.Add(new MapKeywordTerm
                    {
                        Term = family.BusinessConcept,
                        Kind = KeywordTermKinds.Exact,
                        SearchUse = KeywordSearchUses.PrimaryDiscovery
                    });
                }

                families.Add(family);
            }

            return new MapKeywordFamilyGenerationResult
            {
                Families = families,
                IsValid = families.Count > 0
            };
        }
        catch (JsonException ex)
        {
            return InvalidFamilies(ex.Message);
        }
    }

    private static MapSourceCategoryMappingResult TryParseMappings(string raw, MapSourceCategoryMappingRequest request)
    {
        try
        {
            using var doc = JsonDocument.Parse(ExtractJsonObject(raw));
            var mappings = new List<MapSourceCategoryMapping>();
            if (doc.RootElement.TryGetProperty("mappings", out var mappingsEl)
                && mappingsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in mappingsEl.EnumerateArray())
                {
                    var category = ReadString(item, "sourceCategory");
                    if (string.IsNullOrWhiteSpace(category))
                    {
                        continue;
                    }

                    if (!request.AvailableTags.Any(x =>
                            string.Equals(x.Tag, category, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    mappings.Add(new MapSourceCategoryMapping
                    {
                        FamilyKey = ReadString(item, "familyKey") ?? string.Empty,
                        OriginatingRequirement = ReadString(item, "originatingRequirement") ?? string.Empty,
                        SourceName = "OpenStreetMapOverpass",
                        SourceCategory = category,
                        MatchKind = ReadString(item, "matchKind") ?? SourceCategoryMatchKinds.Approximate,
                        RelevanceReason = ReadString(item, "relevanceReason") ?? "Ollama proposed mapping.",
                        AmbiguityNotes = ReadString(item, "ambiguityNotes"),
                        IsSupported = ReadBool(item, "isSupported", true)
                    });
                }
            }

            return new MapSourceCategoryMappingResult
            {
                Mappings = mappings,
                UnsupportedConcepts = doc.RootElement.TryGetProperty("unsupportedConcepts", out _)
                    ? ReadStringArray(doc.RootElement, "unsupportedConcepts")
                    : [],
                IsValid = mappings.Count > 0
                          || (doc.RootElement.TryGetProperty("unsupportedConcepts", out var unsupported)
                              && unsupported.ValueKind == JsonValueKind.Array)
            };
        }
        catch (JsonException ex)
        {
            return InvalidMappings(ex.Message);
        }
    }

    private static string ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return raw;
        }

        return raw[start..(end + 1)];
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static List<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static MapKeywordFamilyGenerationResult InvalidFamilies(string error)
        => new() { IsValid = false, ErrorSummary = error };

    private static MapSourceCategoryMappingResult InvalidMappings(string error)
        => new() { IsValid = false, ErrorSummary = error };
}
