using Sendly.Application.Abstractions;

namespace Sendly.Application.Services.MapResearch;

public class MapPlaceQualificationResult
{
    public bool Accepted { get; set; }

    public bool NeedsReview { get; set; }

    public string Disposition { get; set; } = "accepted";

    public string Reason { get; set; } = string.Empty;

    public string? AssignedIndustry { get; set; }
}

public static class MapPlaceQualifier
{
    public static MapPlaceQualificationResult Evaluate(MapPlaceRecord place, MapResearchPlan plan)
    {
        var spec = plan.Specification;
        if (string.IsNullOrWhiteSpace(place.Name))
        {
            return Reject("rejected_missing_name", "Map place is missing a name.");
        }

        if (place.Latitude is null || place.Longitude is null)
        {
            return Reject("rejected_missing_coordinates", "Map place is missing coordinates.");
        }

        if (!IsValidCoordinate(place.Latitude.Value, place.Longitude.Value))
        {
            return Reject("rejected_invalid_coordinates", "Map place coordinates are invalid.");
        }

        var haystack = BuildHaystack(place);
        var exclusion = FindExclusion(haystack, spec);
        if (exclusion is not null)
        {
            return Reject("rejected_exclusion", $"Place matches project exclusion '{exclusion}'.");
        }

        var family = plan.KeywordFamilies.FirstOrDefault(x =>
            string.Equals(x.FamilyKey, place.KeywordFamilyKey, StringComparison.OrdinalIgnoreCase));
        var matchedIndustry = MatchIndustry(haystack, place, family, spec);
        if (matchedIndustry is null && family is { IsMandatory: true })
        {
            return NeedsReviewResult(
                "needs_review_industry",
                "Place was discovered geographically but industry relevance still needs verification.",
                family.BusinessConcept);
        }

        if (matchedIndustry is null)
        {
            return NeedsReviewResult(
                "needs_review_weak_match",
                "Place lacks strong industry evidence from map tags or name.",
                family?.BusinessConcept ?? spec.ApprovedIndustries.FirstOrDefault());
        }

        return new MapPlaceQualificationResult
        {
            Accepted = true,
            Disposition = "discovered",
            Reason = "Place matches project geography and concept mapping from source tags/name.",
            AssignedIndustry = matchedIndustry
        };
    }

    private static string BuildHaystack(MapPlaceRecord place)
        => $"{place.Name} {place.AlternativeName} {place.CategoryTags} {place.MatchedSourceCategory} {place.StreetAddress} {place.City}";

    private static string? FindExclusion(string haystack, ResearchSpecification spec)
    {
        foreach (var excluded in spec.ExcludedIndustries.Concat(spec.ExcludedKeywords).Concat(spec.ExcludedEntityTypes))
        {
            if (!string.IsNullOrWhiteSpace(excluded)
                && haystack.Contains(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return excluded;
            }
        }

        return null;
    }

    private static string? MatchIndustry(
        string haystack,
        MapPlaceRecord place,
        MapKeywordFamily? family,
        ResearchSpecification spec)
    {
        if (family is not null)
        {
            foreach (var term in family.Terms)
            {
                if (haystack.Contains(term.Term, StringComparison.OrdinalIgnoreCase))
                {
                    return family.BusinessConcept;
                }
            }

            if (!string.IsNullOrWhiteSpace(place.MatchedSourceCategory)
                && place.MatchedSourceCategory.Contains(
                    family.BusinessConcept.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "___",
                    StringComparison.OrdinalIgnoreCase))
            {
                return family.BusinessConcept;
            }
        }

        foreach (var industry in spec.ApprovedIndustries)
        {
            if (haystack.Contains(industry, StringComparison.OrdinalIgnoreCase))
            {
                return industry;
            }

            foreach (var synonym in IndustrySynonymCatalog.Expand([industry]))
            {
                if (haystack.Contains(synonym, StringComparison.OrdinalIgnoreCase))
                {
                    return industry;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(place.MatchedSourceCategory))
        {
            return family?.BusinessConcept ?? spec.ApprovedIndustries.FirstOrDefault();
        }

        return null;
    }

    private static bool IsValidCoordinate(double latitude, double longitude)
        => latitude is >= -90 and <= 90
           && longitude is >= -180 and <= 180
           && !(Math.Abs(latitude) < 0.000001 && Math.Abs(longitude) < 0.000001);

    private static MapPlaceQualificationResult Reject(string disposition, string reason)
        => new()
        {
            Accepted = false,
            Disposition = disposition,
            Reason = reason
        };

    private static MapPlaceQualificationResult NeedsReviewResult(string disposition, string reason, string? industry)
        => new()
        {
            Accepted = true,
            NeedsReview = true,
            Disposition = disposition,
            Reason = reason,
            AssignedIndustry = industry
        };
}
