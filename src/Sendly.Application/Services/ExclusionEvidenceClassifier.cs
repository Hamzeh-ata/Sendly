using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public static class ExclusionEvidenceClassifier
{
    private static readonly string[] SchoolTerms =
    [
        "school", "schools", "k-12", "k12", "kindergarten", "nursery", "elementary school",
        "middle school", "high school", "primary school", "secondary school", "academy",
        "university", "universities", "college", "colleges", "school district",
        "language school", "tutoring", "admission", "tuition",
        "مدرسة", "مدارس", "جامعة", "جامعات", "كلية", "كليات", "تعليم", "روضة", "أكاديمية",
        "schule", "schulen", "grundschule", "realschule", "gesamtschule", "hochschule",
        "universität", "universitaet", "volksschule", "gymnasium schule", "schüler", "schueler"
    ];

    private static readonly string[] DirectoryTerms =
    [
        "business directory", "school directory", "member directory", "yellow pages",
        "listings", "find a school", "find a business", "دليل", "صفحات صفراء"
    ];

    private static readonly string[] GovernmentTerms =
    [
        "government", "ministry", "municipality", "authority", "department of education",
        "public sector", ".gov", "هيئة", "وزارة", "بلدية", "حكومة", "behörde", "ministerium"
    ];

    private static readonly string[] BookingPlatformTerms =
    [
        "booking platform", "classpass", "fresha", "lead generation", "software vendor"
    ];

    private static readonly string[] HotelRestaurantTerms =
    [
        "hotel", "resort", "restaurant", "travel agency", "tour operator"
    ];

    private static readonly string[] NonEducationSchoolPhrases =
    [
        "sailing school", "dive school", "diving school", "scuba school", "ski school",
        "surf school", "swim school", "driving school", "flight school", "cooking school",
        "dance school", "music school", "yoga school", "martial arts school"
    ];

    private static readonly HashSet<string> WeakEducationCollateralTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "university", "universities", "college", "colleges", "tutoring", "careers",
        "recruitment", "jobs", "admission", "tuition"
    };

    public static ExclusionClassificationResult Classify(
        string? companyName,
        string? title,
        string? description,
        string? websiteOrUrl,
        IReadOnlyList<string>? excludedIndustries = null,
        IReadOnlyList<string>? excludedKeywords = null,
        IReadOnlyList<string>? approvedIndustries = null)
    {
        var haystack = Normalize($"{companyName} {title} {description} {websiteOrUrl}");
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return new ExclusionClassificationResult();
        }

        var stripped = StripNonEducationSchoolPhrases(haystack);
        var excludeSchools = HasBroadEducationExclusion(excludedIndustries, excludedKeywords);

        if (excludeSchools && ContainsAny(stripped, SchoolTerms))
        {
            return Excluded("school", "exclusion.school", "Evidence matches an education or school entity.");
        }

        if (ContainsAny(haystack, GovernmentTerms)
            && (ContainsAny(haystack, SchoolTerms.Concat(["education", "تعليم"]))
                || LooksLikeGovernmentName(companyName)
                || LooksLikeGovernmentName(title)))
        {
            return Excluded("government authority", "exclusion.government", "Evidence matches a government or education authority.");
        }

        if (ContainsAny(haystack, DirectoryTerms)
            || EntityClassifier.Classify(companyName, title, description, websiteOrUrl).EntityType
                == ResearchEntityType.DirectoryAggregator)
        {
            var entity = EntityClassifier.Classify(companyName, title, description, websiteOrUrl);
            if (entity.EntityType is ResearchEntityType.DirectoryAggregator or ResearchEntityType.PublisherEditorial)
            {
                return Excluded(
                    entity.EntityType == ResearchEntityType.PublisherEditorial ? "article" : "directory",
                    entity.ReasonCode,
                    entity.Explanation);
            }
        }

        if (ContainsAny(haystack, BookingPlatformTerms))
        {
            return Excluded("booking platform", "exclusion.booking_platform", "Evidence matches a booking platform or vendor.");
        }

        if (ContainsAny(haystack, HotelRestaurantTerms))
        {
            return Excluded("hotel or restaurant", "exclusion.hospitality", "Evidence matches an excluded hotel, restaurant, or travel agency.");
        }

        var configured = new List<string>();
        if (excludedIndustries is not null)
        {
            configured.AddRange(excludedIndustries);
        }

        if (excludedKeywords is not null)
        {
            configured.AddRange(excludedKeywords);
        }

        var educationTarget = IsEducationTarget(approvedIndustries);
        var matchesApprovedEducation = educationTarget
                                       && IndustrySynonymCatalog.MatchesAny(haystack, approvedIndustries ?? []);

        foreach (var term in configured)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 4)
            {
                continue;
            }

            var normalizedTerm = Normalize(term);
            if (matchesApprovedEducation && IsWeakEducationCollateralTerm(normalizedTerm))
            {
                continue;
            }

            if (stripped.Contains(normalizedTerm, StringComparison.Ordinal))
            {
                return Excluded(term.Trim(), "exclusion.configured_term", $"Evidence matched excluded term '{term.Trim()}'.");
            }
        }

        return new ExclusionClassificationResult();
    }

    private static bool IsWeakEducationCollateralTerm(string normalizedTerm)
        => WeakEducationCollateralTerms.Contains(normalizedTerm);

    private static bool IsEducationTarget(IReadOnlyList<string>? approvedIndustries)
    {
        if (approvedIndustries is null || approvedIndustries.Count == 0)
        {
            return false;
        }

        foreach (var industry in approvedIndustries)
        {
            if (string.IsNullOrWhiteSpace(industry))
            {
                continue;
            }

            if (industry.Contains("school", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("education", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("academy", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("kindergarten", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("nursery", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("college", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("university", StringComparison.OrdinalIgnoreCase)
                || industry.Contains("مدرسة", StringComparison.Ordinal)
                || industry.Contains("تعليم", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeExcludedSchoolEvidence(string? text)
    {
        var stripped = StripNonEducationSchoolPhrases(Normalize(text));
        return ContainsAny(stripped, SchoolTerms);
    }

    private static bool HasBroadEducationExclusion(
        IReadOnlyList<string>? excludedIndustries,
        IReadOnlyList<string>? excludedKeywords)
    {
        foreach (var term in (excludedIndustries ?? []).Concat(excludedKeywords ?? []))
        {
            if (IsBroadEducationExclusionTerm(term))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBroadEducationExclusionTerm(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        var normalized = Normalize(term);
        return normalized is "school" or "schools" or "education"
            or "مدرسة" or "مدارس" or "تعليم" or "schule" or "schulen";
    }

    private static bool LooksLikeGovernmentName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var value = Normalize(name);
        return value.Contains("authority", StringComparison.Ordinal)
               || value.Contains("ministry", StringComparison.Ordinal)
               || value.Contains("هيئة", StringComparison.Ordinal)
               || value.Contains("وزارة", StringComparison.Ordinal)
               || value.Contains("behörde", StringComparison.Ordinal);
    }

    private static ExclusionClassificationResult Excluded(string entityType, string reason, string explanation)
        => new()
        {
            IsExcluded = true,
            MatchedTerm = entityType,
            EntityType = entityType,
            ReasonCode = reason,
            Explanation = explanation
        };

    private static bool ContainsAny(string haystack, IEnumerable<string> terms)
    {
        foreach (var term in terms)
        {
            if (haystack.Contains(Normalize(term), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string StripNonEducationSchoolPhrases(string value)
    {
        var normalized = value;
        foreach (var phrase in NonEducationSchoolPhrases)
        {
            normalized = normalized.Replace(Normalize(phrase), " ", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();
}
