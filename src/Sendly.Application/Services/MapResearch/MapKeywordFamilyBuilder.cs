using Sendly.Application.Abstractions;

namespace Sendly.Application.Services.MapResearch;

public static class MapKeywordFamilyBuilder
{
    public static List<MapKeywordFamily> BuildDeterministic(MapKeywordFamilyGenerationRequest request)
    {
        var families = new List<MapKeywordFamily>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spec = request.Specification;

        foreach (var industry in spec.ApprovedIndustries)
        {
            AddIndustryFamily(families, seen, industry, true, true, spec);
        }

        foreach (var keyword in request.MandatoryKeywords)
        {
            AddKeywordFamily(families, seen, keyword, true, true, spec);
        }

        foreach (var keyword in request.ProjectKeywords)
        {
            AddKeywordFamily(families, seen, keyword, true, false, spec);
        }

        foreach (var keyword in request.OptionalKeywords)
        {
            AddKeywordFamily(families, seen, keyword, false, false, spec);
        }

        foreach (var synonym in spec.IndustrySynonyms)
        {
            AttachSynonym(families, synonym, spec);
        }

        AttachLanguageVariants(families, spec);
        return families;
    }

    public static List<MapKeywordFamily> MergeAiFamilies(
        List<MapKeywordFamily> deterministic,
        List<MapKeywordFamily> aiFamilies,
        ResearchSpecification spec)
    {
        var result = deterministic.Select(CloneFamily).ToList();
        foreach (var aiFamily in aiFamilies)
        {
            if (string.IsNullOrWhiteSpace(aiFamily.BusinessConcept))
            {
                continue;
            }

            if (ViolatesExclusions(aiFamily, spec))
            {
                continue;
            }

            var existing = FindFamily(result, aiFamily.BusinessConcept, aiFamily.OriginatingRequirement);
            if (existing is null)
            {
                if (!IsAlignedWithTargets(aiFamily, spec))
                {
                    continue;
                }

                result.Add(SanitizeFamily(aiFamily, spec));
                continue;
            }

            MergeTerms(existing, aiFamily, spec);
        }

        return result;
    }

    private static void AddIndustryFamily(
        List<MapKeywordFamily> families,
        HashSet<string> seen,
        string industry,
        bool isPrimary,
        bool isMandatory,
        ResearchSpecification spec)
    {
        if (string.IsNullOrWhiteSpace(industry) || !seen.Add(industry.Trim()))
        {
            return;
        }

        if (IsExcludedTerm(industry, spec))
        {
            return;
        }

        families.Add(new MapKeywordFamily
        {
            FamilyKey = BuildFamilyKey("industry", industry),
            OriginatingRequirement = industry.Trim(),
            BusinessConcept = industry.Trim(),
            IsPrimaryTarget = isPrimary,
            IsMandatory = isMandatory,
            Terms =
            [
                new MapKeywordTerm
                {
                    Term = industry.Trim(),
                    Kind = KeywordTermKinds.Exact,
                    SearchUse = KeywordSearchUses.PrimaryDiscovery
                }
            ],
            KnownExclusions = spec.ExcludedIndustries.Concat(spec.ExcludedKeywords).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        });
    }

    private static void AddKeywordFamily(
        List<MapKeywordFamily> families,
        HashSet<string> seen,
        string keyword,
        bool isPrimary,
        bool isMandatory,
        ResearchSpecification spec)
    {
        if (string.IsNullOrWhiteSpace(keyword) || !seen.Add(keyword.Trim()))
        {
            return;
        }

        if (IsExcludedTerm(keyword, spec))
        {
            return;
        }

        if (LooksLikeIndustryDuplicate(families, keyword))
        {
            AttachSynonym(families, keyword, spec);
            return;
        }

        families.Add(new MapKeywordFamily
        {
            FamilyKey = BuildFamilyKey("keyword", keyword),
            OriginatingRequirement = keyword.Trim(),
            BusinessConcept = keyword.Trim(),
            IsPrimaryTarget = isPrimary,
            IsMandatory = isMandatory,
            Terms =
            [
                new MapKeywordTerm
                {
                    Term = keyword.Trim(),
                    Kind = KeywordTermKinds.Exact,
                    SearchUse = isMandatory ? KeywordSearchUses.PrimaryDiscovery : KeywordSearchUses.AlternateDiscovery
                }
            ],
            KnownExclusions = spec.ExcludedKeywords.ToList()
        });
    }

    private static void AttachSynonym(List<MapKeywordFamily> families, string synonym, ResearchSpecification spec)
    {
        if (string.IsNullOrWhiteSpace(synonym) || IsExcludedTerm(synonym, spec))
        {
            return;
        }

        var target = FindBestFamilyForTerm(families, synonym) ?? families.FirstOrDefault();
        if (target is null)
        {
            return;
        }

        if (TermExists(target, synonym))
        {
            return;
        }

        target.Terms.Add(new MapKeywordTerm
        {
            Term = synonym.Trim(),
            Kind = KeywordTermKinds.Synonym,
            SearchUse = KeywordSearchUses.AlternateDiscovery
        });
    }

    private static void AttachLanguageVariants(List<MapKeywordFamily> families, ResearchSpecification spec)
    {
        foreach (var family in families)
        {
            foreach (var hint in ArabicIndustryLexicon.TranslateIndustry(family.BusinessConcept))
            {
                if (TermExists(family, hint) || IsExcludedTerm(hint, spec))
                {
                    continue;
                }

                family.Terms.Add(new MapKeywordTerm
                {
                    Term = hint,
                    Kind = KeywordTermKinds.Translation,
                    Language = "ar",
                    SearchUse = KeywordSearchUses.AlternateDiscovery
                });
            }
        }
    }

    private static bool LooksLikeIndustryDuplicate(List<MapKeywordFamily> families, string keyword)
    {
        foreach (var family in families)
        {
            if (string.Equals(family.BusinessConcept, keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static MapKeywordFamily? FindBestFamilyForTerm(List<MapKeywordFamily> families, string term)
    {
        foreach (var family in families)
        {
            if (ContainsToken(family.BusinessConcept, term) || ContainsToken(term, family.BusinessConcept))
            {
                return family;
            }
        }

        return null;
    }

    private static bool ContainsToken(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return left.Contains(right, StringComparison.OrdinalIgnoreCase)
               || right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TermExists(MapKeywordFamily family, string term)
    {
        foreach (var existing in family.Terms)
        {
            if (string.Equals(existing.Term, term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExcludedTerm(string term, ResearchSpecification spec)
    {
        foreach (var excluded in spec.ExcludedIndustries.Concat(spec.ExcludedKeywords).Concat(spec.ExcludedEntityTypes))
        {
            if (string.Equals(excluded, term, StringComparison.OrdinalIgnoreCase)
                || term.Contains(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ViolatesExclusions(MapKeywordFamily family, ResearchSpecification spec)
    {
        if (IsExcludedTerm(family.BusinessConcept, spec) || IsExcludedTerm(family.OriginatingRequirement, spec))
        {
            return true;
        }

        foreach (var term in family.Terms)
        {
            if (IsExcludedTerm(term.Term, spec))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAlignedWithTargets(MapKeywordFamily family, ResearchSpecification spec)
    {
        if (spec.ApprovedIndustries.Count == 0)
        {
            return true;
        }

        foreach (var industry in spec.ApprovedIndustries)
        {
            if (ContainsToken(family.BusinessConcept, industry) || ContainsToken(industry, family.BusinessConcept))
            {
                return true;
            }
        }

        return false;
    }

    private static MapKeywordFamily SanitizeFamily(MapKeywordFamily family, ResearchSpecification spec)
    {
        var clone = CloneFamily(family);
        clone.Terms = clone.Terms
            .Where(x => !string.IsNullOrWhiteSpace(x.Term) && !IsExcludedTerm(x.Term, spec))
            .ToList();
        if (clone.Terms.Count == 0)
        {
            clone.Terms.Add(new MapKeywordTerm
            {
                Term = clone.BusinessConcept,
                Kind = KeywordTermKinds.Exact,
                SearchUse = KeywordSearchUses.PrimaryDiscovery
            });
        }

        clone.FamilyKey = string.IsNullOrWhiteSpace(clone.FamilyKey)
            ? BuildFamilyKey("ai", clone.BusinessConcept)
            : clone.FamilyKey;
        return clone;
    }

    private static void MergeTerms(MapKeywordFamily existing, MapKeywordFamily incoming, ResearchSpecification spec)
    {
        foreach (var term in incoming.Terms)
        {
            if (string.IsNullOrWhiteSpace(term.Term) || IsExcludedTerm(term.Term, spec) || TermExists(existing, term.Term))
            {
                continue;
            }

            existing.Terms.Add(new MapKeywordTerm
            {
                Term = term.Term.Trim(),
                Kind = string.IsNullOrWhiteSpace(term.Kind) ? KeywordTermKinds.Synonym : term.Kind,
                Language = term.Language,
                SearchUse = string.IsNullOrWhiteSpace(term.SearchUse)
                    ? KeywordSearchUses.AlternateDiscovery
                    : term.SearchUse,
                AmbiguityNotes = term.AmbiguityNotes
            });
        }
    }

    private static MapKeywordFamily? FindFamily(List<MapKeywordFamily> families, string concept, string originating)
    {
        foreach (var family in families)
        {
            if (string.Equals(family.BusinessConcept, concept, StringComparison.OrdinalIgnoreCase)
                || string.Equals(family.OriginatingRequirement, originating, StringComparison.OrdinalIgnoreCase))
            {
                return family;
            }
        }

        return null;
    }

    private static MapKeywordFamily CloneFamily(MapKeywordFamily source)
    {
        return new MapKeywordFamily
        {
            FamilyKey = source.FamilyKey,
            OriginatingRequirement = source.OriginatingRequirement,
            BusinessConcept = source.BusinessConcept,
            IsPrimaryTarget = source.IsPrimaryTarget,
            IsMandatory = source.IsMandatory,
            Terms = source.Terms.Select(x => new MapKeywordTerm
            {
                Term = x.Term,
                Kind = x.Kind,
                Language = x.Language,
                SearchUse = x.SearchUse,
                AmbiguityNotes = x.AmbiguityNotes
            }).ToList(),
            KnownExclusions = source.KnownExclusions.ToList(),
            Ambiguities = source.Ambiguities.ToList()
        };
    }

    private static string BuildFamilyKey(string prefix, string value)
    {
        var cleaned = new string(value.Trim().ToLowerInvariant().Select(ch =>
            char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        return $"{prefix}:{cleaned}";
    }
}
