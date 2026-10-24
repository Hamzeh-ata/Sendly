using Sendly.Application.Abstractions;

namespace Sendly.Application.Services.MapResearch;

public static class OsmSourceCategoryMapper
{
    public static MapSourceCategoryMappingResult MapDeterministic(MapSourceCategoryMappingRequest request)
    {
        var mappings = new List<MapSourceCategoryMapping>();
        var unsupported = new List<string>();
        var catalog = request.AvailableTags.Count > 0
            ? request.AvailableTags
            : OsmTagCapabilityCatalog.All.ToList();

        foreach (var family in request.KeywordFamilies)
        {
            var tokens = CollectFamilyTokens(family);
            var matches = FindMatchingCapabilities(catalog, tokens);
            if (matches.Count == 0)
            {
                unsupported.Add(family.BusinessConcept);
                mappings.Add(new MapSourceCategoryMapping
                {
                    FamilyKey = family.FamilyKey,
                    OriginatingRequirement = family.OriginatingRequirement,
                    SourceName = "OpenStreetMapOverpass",
                    SourceCategory = string.Empty,
                    MatchKind = SourceCategoryMatchKinds.Unsupported,
                    RelevanceReason = "No verified OSM tag matched the project concept tokens.",
                    IsSupported = false
                });
                continue;
            }

            foreach (var match in matches)
            {
                if (IsExcludedByProject(match, request.Specification))
                {
                    continue;
                }

                mappings.Add(new MapSourceCategoryMapping
                {
                    FamilyKey = family.FamilyKey,
                    OriginatingRequirement = family.OriginatingRequirement,
                    SourceName = "OpenStreetMapOverpass",
                    SourceCategory = match.Tag,
                    MatchKind = ResolveMatchKind(family, match),
                    RelevanceReason =
                        $"Mapped project concept '{family.BusinessConcept}' to OSM tag '{match.Tag}' ({match.Description}).",
                    AmbiguityNotes = match.ConceptTokens.Count > 4
                        ? "Tag concept coverage is broad; qualification must re-check project fit."
                        : null,
                    IsSupported = true
                });
            }
        }

        return new MapSourceCategoryMappingResult
        {
            Mappings = mappings,
            UnsupportedConcepts = unsupported,
            IsValid = mappings.Any(x => x.IsSupported)
                      || request.KeywordFamilies.Count == 0
        };
    }

    public static MapSourceCategoryMappingResult MergeAiMappings(
        MapSourceCategoryMappingResult deterministic,
        MapSourceCategoryMappingResult aiResult,
        ResearchSpecification spec,
        IReadOnlyList<OsmTagCapability> catalog)
    {
        var result = new MapSourceCategoryMappingResult
        {
            Mappings = deterministic.Mappings.Select(CloneMapping).ToList(),
            UnsupportedConcepts = deterministic.UnsupportedConcepts.ToList(),
            IsValid = deterministic.IsValid
        };

        if (!aiResult.IsValid)
        {
            return result;
        }

        foreach (var mapping in aiResult.Mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.SourceCategory))
            {
                continue;
            }

            var capability = OsmTagCapabilityCatalog.FindExactTag(mapping.SourceCategory)
                             ?? catalog.FirstOrDefault(x =>
                                 string.Equals(x.Tag, mapping.SourceCategory, StringComparison.OrdinalIgnoreCase));
            if (capability is null)
            {
                continue;
            }

            if (IsExcludedByProject(capability, spec))
            {
                continue;
            }

            if (AlreadyMapped(result.Mappings, mapping.FamilyKey, capability.Tag))
            {
                continue;
            }

            result.Mappings.Add(new MapSourceCategoryMapping
            {
                FamilyKey = mapping.FamilyKey,
                OriginatingRequirement = mapping.OriginatingRequirement,
                SourceName = "OpenStreetMapOverpass",
                SourceCategory = capability.Tag,
                MatchKind = string.IsNullOrWhiteSpace(mapping.MatchKind)
                    ? SourceCategoryMatchKinds.Approximate
                    : mapping.MatchKind,
                RelevanceReason = string.IsNullOrWhiteSpace(mapping.RelevanceReason)
                    ? $"AI-proposed mapping to '{capability.Tag}'."
                    : mapping.RelevanceReason,
                AmbiguityNotes = mapping.AmbiguityNotes,
                IsSupported = true
            });
            result.UnsupportedConcepts.RemoveAll(x =>
                string.Equals(x, mapping.OriginatingRequirement, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x, mapping.FamilyKey, StringComparison.OrdinalIgnoreCase));
        }

        result.IsValid = result.Mappings.Any(x => x.IsSupported);
        return result;
    }

    private static List<string> CollectFamilyTokens(MapKeywordFamily family)
    {
        var tokens = new List<string> { family.BusinessConcept, family.OriginatingRequirement };
        foreach (var term in family.Terms)
        {
            tokens.Add(term.Term);
        }

        return tokens;
    }

    private static List<OsmTagCapability> FindMatchingCapabilities(
        IReadOnlyList<OsmTagCapability> catalog,
        IReadOnlyList<string> tokens)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            foreach (var part in SplitTokens(token))
            {
                normalized.Add(part);
            }
        }

        var scored = new List<(OsmTagCapability Cap, int Score)>();
        foreach (var capability in catalog)
        {
            var score = 0;
            foreach (var conceptToken in capability.ConceptTokens)
            {
                if (normalized.Contains(conceptToken))
                {
                    score += 2;
                }
            }

            foreach (var token in normalized)
            {
                if (capability.Description.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || capability.Tag.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    score += 1;
                }
            }

            if (score > 0)
            {
                scored.Add((capability, score));
            }
        }

        scored.Sort((left, right) => right.Score.CompareTo(left.Score));
        var result = new List<OsmTagCapability>();
        foreach (var item in scored)
        {
            if (result.Count >= 6)
            {
                break;
            }

            result.Add(item.Cap);
        }

        return result;
    }

    private static IEnumerable<string> SplitTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var part in value.Split([' ', '-', '_', '/', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var cleaned = part.Trim().ToLowerInvariant();
            if (cleaned.Length >= 3)
            {
                yield return cleaned;
            }
        }
    }

    private static string ResolveMatchKind(MapKeywordFamily family, OsmTagCapability capability)
    {
        foreach (var token in capability.ConceptTokens)
        {
            if (string.Equals(token, family.BusinessConcept, StringComparison.OrdinalIgnoreCase))
            {
                return SourceCategoryMatchKinds.Exact;
            }
        }

        return SourceCategoryMatchKinds.Approximate;
    }

    private static bool IsExcludedByProject(OsmTagCapability capability, ResearchSpecification spec)
    {
        var haystack = $"{capability.Tag} {capability.Description} {string.Join(' ', capability.ConceptTokens)}";
        foreach (var excluded in spec.ExcludedIndustries.Concat(spec.ExcludedKeywords).Concat(spec.ExcludedEntityTypes))
        {
            if (string.IsNullOrWhiteSpace(excluded))
            {
                continue;
            }

            if (haystack.Contains(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AlreadyMapped(List<MapSourceCategoryMapping> mappings, string familyKey, string tag)
    {
        foreach (var mapping in mappings)
        {
            if (string.Equals(mapping.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(mapping.SourceCategory, tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static MapSourceCategoryMapping CloneMapping(MapSourceCategoryMapping source)
    {
        return new MapSourceCategoryMapping
        {
            FamilyKey = source.FamilyKey,
            OriginatingRequirement = source.OriginatingRequirement,
            SourceName = source.SourceName,
            SourceCategory = source.SourceCategory,
            MatchKind = source.MatchKind,
            RelevanceReason = source.RelevanceReason,
            AmbiguityNotes = source.AmbiguityNotes,
            IsSupported = source.IsSupported
        };
    }
}
