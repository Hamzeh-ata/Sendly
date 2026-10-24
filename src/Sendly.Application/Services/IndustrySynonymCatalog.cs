namespace Sendly.Application.Services;

public static class IndustrySynonymCatalog
{
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cleaning"] =
        [
            "commercial cleaning", "cleaning services", "janitorial", "facility cleaning",
            "sanitation", "نظافة", "شركات نظافة", "تنظيف", "خدمات النظافة"
        ],
        ["commercial cleaning"] =
        [
            "cleaning", "cleaning services", "janitorial", "facility management",
            "building cleaning", "نظافة تجارية", "شركات نظافة", "تنظيف منشآت"
        ],
        ["education"] =
        [
            "school", "schools", "private school", "independent school", "international school",
            "academy", "كلية", "تعليم", "مدارس", "مدرسة", "تعليم خاص", "fee-paying school",
            "boarding school", "preparatory school", "prep school"
        ],
        ["school"] =
        [
            "education", "private school", "independent school", "schools", "academy",
            "مدارس", "مدرسة", "international school", "boarding school"
        ],
        ["construction"] =
        [
            "contractor", "building", "engineering", "civil", "مقاولات", "إنشاءات", "بناء"
        ],
        ["hospitality"] =
        [
            "hotel", "hotels", "tourism", "ضيافة", "فنادق", "فندق"
        ],
        ["facility"] =
        [
            "facilities", "facility management", "facilities management",
            "إدارة مرافق", "مرافق"
        ],
        ["healthcare"] =
        [
            "clinic", "hospital", "medical", "صحة", "عيادة", "مستشفى"
        ],
        ["logistics"] =
        [
            "transport", "shipping", "freight", "نقل", "شحن", "لوجستيات"
        ],
        ["retail"] =
        [
            "store", "shop", "تجزئة", "متجر", "محل"
        ],
        ["gym"] =
        [
            "fitness", "fitness club", "health club", "fitness center", "fitness centre",
            "fitness studio", "training facility", "crossfit", "boutique fitness",
            "personal training", "sports club", "صالة رياضية", "نادي رياضي"
        ],
        ["fitness"] =
        [
            "gym", "fitness club", "health club", "fitness center", "fitness centre",
            "fitness studio", "training facility", "crossfit", "boutique fitness",
            "personal training", "strength and conditioning"
        ],
        ["escape room"] =
        [
            "escape game", "escape rooms", "exit game", "escape room operator"
        ],
        ["archery club"] =
        [
            "archery range", "archery venue", "archery", "bow range"
        ],
        ["pottery studio"] =
        [
            "ceramics studio", "pottery class", "clay studio", "art studio"
        ],
        ["art studio"] =
        [
            "pottery studio", "ceramics studio", "art workshop"
        ],
        ["kayak rental"] =
        [
            "canoe rental", "kayak hire", "paddle rental", "paddleboard rental"
        ],
        ["climbing gym"] =
        [
            "bouldering gym", "rock climbing gym", "climbing centre", "climbing center"
        ],
        ["photography studio"] =
        [
            "photo studio", "photography studio rental", "studio booking"
        ],
        ["sailing center"] =
        [
            "sailing centre", "sailing school", "sail training", "yacht club"
        ],
        ["dive center"] =
        [
            "dive centre", "scuba dive center", "diving center", "dive school"
        ],
        ["go-kart track"] =
        [
            "karting track", "go kart", "karting venue", "go-kart"
        ],
        ["event equipment rental"] =
        [
            "party equipment rental", "event rental", "party rental"
        ],
        ["bicycle rental"] =
        [
            "bike rental", "bicycle hire", "cycle rental"
        ],
        ["bicycle workshop"] =
        [
            "bike workshop", "bicycle repair", "bike repair shop"
        ],
        ["billiards club"] =
        [
            "billiard club", "billiards hall", "billiard hall", "billiard parlor",
            "billiards parlor", "snooker club", "snooker hall", "pool hall",
            "pool parlor", "pool tables"
        ],
        ["snooker club"] =
        [
            "snooker hall", "billiards club", "billiard club", "pool hall", "pool parlor"
        ],
        ["pool hall"] =
        [
            "pool parlor", "billiard parlor", "billiards parlor", "billiards club",
            "billiard club", "snooker club", "snooker hall", "pool tables"
        ],
        ["paintball arena"] =
        [
            "paintball field", "paintball park", "paintball venue", "paintball",
            "paintball games"
        ],
        ["laser tag arena"] =
        [
            "laser tag", "laser tag venue", "laser tag center", "laser tag centre",
            "laser tag games"
        ],
        ["trampoline park"] =
        [
            "indoor trampoline park", "trampoline venue", "trampoline",
            "indoor trampoline"
        ],
        ["indoor activity center"] =
        [
            "indoor activity centre", "indoor activity park", "indoor adventure park"
        ]
    };

    private static readonly Dictionary<string, string> FamilyByToken = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cleaning"] = "cleaning",
        ["janitorial"] = "cleaning",
        ["sanitation"] = "cleaning",
        ["نظافة"] = "cleaning",
        ["تنظيف"] = "cleaning",
        ["education"] = "education",
        ["school"] = "education",
        ["schools"] = "education",
        ["academy"] = "education",
        ["كلية"] = "education",
        ["تعليم"] = "education",
        ["مدارس"] = "education",
        ["مدرسة"] = "education",
        ["construction"] = "construction",
        ["contractor"] = "construction",
        ["building"] = "construction",
        ["engineering"] = "construction",
        ["hospitality"] = "hospitality",
        ["hotel"] = "hospitality",
        ["hotels"] = "hospitality",
        ["facility"] = "facility",
        ["facilities"] = "facility",
        ["healthcare"] = "healthcare",
        ["clinic"] = "healthcare",
        ["hospital"] = "healthcare",
        ["medical"] = "healthcare",
        ["logistics"] = "logistics",
        ["retail"] = "retail",
        ["store"] = "retail",
        ["shop"] = "retail",
        ["gym"] = "fitness",
        ["fitness"] = "fitness",
        ["crossfit"] = "fitness",
        ["صالة"] = "fitness",
        ["نادي"] = "fitness",
        ["billiards"] = "billiards",
        ["snooker"] = "billiards",
        ["paintball"] = "paintball",
        ["trampoline"] = "trampoline"
    };

    private static readonly HashSet<string> StopTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "the", "of", "for", "a", "an", "in", "to", "or", "with",
        "primary", "secondary", "elementary", "high", "senior", "all", "through",
        "fee", "paying", "private", "independent", "international", "boarding",
        "preparatory", "prep", "college", "day", "k", "12"
    };

    private static readonly HashSet<string> BroadIndustryTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "facility", "facilities", "center", "centre", "service", "services",
        "building", "management", "business", "company",
        "industry", "group", "network", "solutions",
        "club", "studio", "rental", "gym", "range", "track", "shop",
        "workshop", "venue", "operator", "hire", "hall", "arena", "park",
        "field", "tables", "games", "indoor"
    };

    private static readonly HashSet<string> WeakOverlapTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "training", "sports", "activity", "activities", "class", "classes"
    };

    public static List<string> Expand(IEnumerable<string> industries)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var industry in industries)
        {
            if (string.IsNullOrWhiteSpace(industry))
            {
                continue;
            }

            var trimmed = industry.Trim();
            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }

            foreach (var pair in Synonyms)
            {
                if (!MatchesKey(trimmed, pair.Key) && !pair.Value.Any(s => MatchesKey(trimmed, s)))
                {
                    continue;
                }

                if (seen.Add(pair.Key))
                {
                    result.Add(pair.Key);
                }

                foreach (var synonym in pair.Value)
                {
                    if (seen.Add(synonym))
                    {
                        result.Add(synonym);
                    }
                }
            }
        }

        return result;
    }

    public static string? MatchIndustry(string? haystack, IEnumerable<string> industries)
    {
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return null;
        }

        var expanded = Expand(industries);
        foreach (var industry in expanded)
        {
            if (string.IsNullOrWhiteSpace(industry) || industry.Length < 3)
            {
                continue;
            }

            if (haystack.Contains(industry, StringComparison.OrdinalIgnoreCase))
            {
                return industry;
            }
        }

        return null;
    }

    public static bool MatchesAny(string? haystack, IEnumerable<string> industries)
        => !string.IsNullOrWhiteSpace(MatchIndustry(haystack, industries));

    public static string? FindIndustryOverlap(
        IReadOnlyList<string> targetIndustries,
        IReadOnlyList<string> sourceIndustries)
    {
        if (targetIndustries.Count == 0 || sourceIndustries.Count == 0)
        {
            return null;
        }

        var targetExpanded = Expand(targetIndustries);
        var sourceExpanded = Expand(sourceIndustries);

        foreach (var target in targetExpanded)
        {
            foreach (var source in sourceExpanded)
            {
                if (LabelsAlign(target, source))
                {
                    return $"{target}↔{source}";
                }
            }
        }

        var targetFamilies = ResolveFamilies(targetExpanded);
        var sourceFamilies = ResolveFamilies(sourceExpanded);
        foreach (var family in targetFamilies)
        {
            if (sourceFamilies.Contains(family))
            {
                return $"family:{family}";
            }
        }

        return null;
    }

    public static bool MatchesIndustryScope(
        IReadOnlyList<string> targetIndustries,
        IReadOnlyList<string> sourceIndustries)
        => FindIndustryOverlap(targetIndustries, sourceIndustries) is not null;

    public static bool MatchesCountryScope(
        IReadOnlyList<string> targetCountries,
        IReadOnlyList<string> sourceCountries)
    {
        if (targetCountries.Count == 0)
        {
            return true;
        }

        if (sourceCountries.Count == 0)
        {
            return false;
        }

        foreach (var target in targetCountries)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            foreach (var source in sourceCountries)
            {
                if (string.IsNullOrWhiteSpace(source))
                {
                    continue;
                }

                if (CountryLabelsAlign(target, source))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool LabelsAlign(string left, string right)
    {
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (left.Length >= 4 && right.Length >= 4
            && !IsBroadIndustryLabel(left)
            && !IsBroadIndustryLabel(right)
            && (left.Contains(right, StringComparison.OrdinalIgnoreCase)
                || right.Contains(left, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var leftTokens = MeaningfulTokens(left);
        var rightTokens = MeaningfulTokens(right);
        foreach (var token in leftTokens)
        {
            if (!rightTokens.Contains(token)
                || BroadIndustryTokens.Contains(token)
                || WeakOverlapTokens.Contains(token))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool CountryLabelsAlign(string left, string right)
    {
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftNorm = NormalizeCountryAlias(left);
        var rightNorm = NormalizeCountryAlias(right);
        return leftNorm.Equals(rightNorm, StringComparison.OrdinalIgnoreCase)
               || left.Contains(right, StringComparison.OrdinalIgnoreCase)
               || right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCountryAlias(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "us" or "usa" or "u.s." or "u.s.a." or "america" => "united states",
            "uk" or "gb" or "great britain" or "britain" or "england" => "united kingdom",
            "sa" or "ksa" => "saudi arabia",
            "ae" or "uae" => "united arab emirates",
            _ => trimmed
        };
    }

    private static HashSet<string> MeaningfulTokens(string value)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split([' ', '-', '/', ',', '&', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = part.Trim();
            if (token.Length < 3 || StopTokens.Contains(token))
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    private static HashSet<string> ResolveFamilies(IEnumerable<string> labels)
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            var tokens = MeaningfulTokens(label);
            foreach (var token in tokens)
            {
                if (!FamilyByToken.TryGetValue(token, out var family))
                {
                    continue;
                }

                if (BroadIndustryTokens.Contains(token)
                    && tokens.Count > 1
                    && !IsPrimaryFamilyLabel(label, family, tokens))
                {
                    continue;
                }

                families.Add(family);
            }

            if (FamilyByToken.TryGetValue(label.Trim(), out var direct))
            {
                families.Add(direct);
            }
        }

        return families;
    }

    private static bool MatchesKey(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (key.Length <= 2)
        {
            return value.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        if (value.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var singularValue = Singularize(value);
        var singularKey = Singularize(key);
        if (singularValue.Equals(singularKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (BroadIndustryTokens.Contains(key))
        {
            return IsPrimaryFamilyLabel(value, ResolveFamilyHint(key), MeaningfulTokens(value));
        }

        if (FamilyByToken.TryGetValue(key, out var keyFamily))
        {
            var tokens = MeaningfulTokens(value);
            if (tokens.Count > 1)
            {
                return IsPrimaryFamilyLabel(value, keyFamily, tokens);
            }

            return tokens.Contains(key);
        }

        if (key.Contains(' ', StringComparison.Ordinal) || key.Contains('-', StringComparison.Ordinal))
        {
            return value.Contains(key, StringComparison.OrdinalIgnoreCase);
        }

        if (IsBroadIndustryLabel(value) && key.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.Contains(key, StringComparison.OrdinalIgnoreCase)
               || (key.Length >= 4 && value.Length >= 4 && key.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string Singularize(string value)
    {
        var tokens = value.Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length > 4 && token.EndsWith("ies", StringComparison.Ordinal))
            {
                tokens[i] = token[..^3] + "y";
            }
            else if (token.Length > 3 && token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal))
            {
                tokens[i] = token[..^1];
            }
        }

        return string.Join(' ', tokens);
    }

    private static bool IsBroadIndustryLabel(string value)
    {
        var tokens = MeaningfulTokens(value);
        if (tokens.Count == 0)
        {
            return true;
        }

        return tokens.All(BroadIndustryTokens.Contains);
    }

    private static bool IsPrimaryFamilyLabel(string label, string? family, HashSet<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(family) || tokens.Count == 0)
        {
            return false;
        }

        if (label.Equals(family, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var familyTokenHits = 0;
        foreach (var token in tokens)
        {
            if (FamilyByToken.TryGetValue(token, out var mapped)
                && mapped.Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                familyTokenHits++;
                continue;
            }

            if (!BroadIndustryTokens.Contains(token))
            {
                return false;
            }
        }

        return familyTokenHits > 0;
    }

    private static string? ResolveFamilyHint(string key)
    {
        if (FamilyByToken.TryGetValue(key.Trim(), out var family))
        {
            return family;
        }

        foreach (var token in MeaningfulTokens(key))
        {
            if (FamilyByToken.TryGetValue(token, out family))
            {
                return family;
            }
        }

        return key.Trim().ToLowerInvariant();
    }
}
