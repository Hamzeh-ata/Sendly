using Sendly.Application.Abstractions;

namespace Sendly.Application.Services;

public static class IndustryEvidenceClassifier
{
    public static IndustryClassificationResult Classify(
        string? companyName,
        string? title,
        string? description,
        string? notes,
        IReadOnlyList<string> approvedIndustries,
        IReadOnlyList<string>? excludedIndustries = null)
    {
        var haystack = $"{companyName} {title} {description} {notes}";
        var exclusion = ExclusionEvidenceClassifier.Classify(
            companyName,
            title,
            description,
            notes,
            excludedIndustries,
            approvedIndustries: approvedIndustries);
        if (exclusion.IsExcluded)
        {
            return new IndustryClassificationResult
            {
                AssignedIndustry = exclusion.MatchedTerm,
                MatchedApprovedIndustry = false,
                IsExcluded = true,
                ExcludedIndustry = exclusion.MatchedTerm,
                ReasonCode = exclusion.ReasonCode,
                Explanation = exclusion.Explanation
            };
        }

        if (approvedIndustries.Count == 0)
        {
            return new IndustryClassificationResult
            {
                ReasonCode = "industry.no_approved_targets",
                Explanation = "No approved industries were supplied for independent classification."
            };
        }

        var matched = MatchBestApprovedIndustry(haystack, approvedIndustries);
        if (!string.IsNullOrWhiteSpace(matched))
        {
            return new IndustryClassificationResult
            {
                AssignedIndustry = matched,
                MatchedApprovedIndustry = true,
                ReasonCode = "industry.evidence_match",
                Explanation = $"Page evidence independently matched approved industry '{matched}'."
            };
        }

        return new IndustryClassificationResult
        {
            ReasonCode = "industry.no_evidence_match",
            Explanation = "Page evidence did not independently match any approved industry."
        };
    }

    private static string? MatchBestApprovedIndustry(string haystack, IReadOnlyList<string> approvedIndustries)
    {
        string? best = null;
        var bestLength = 0;
        foreach (var industry in approvedIndustries)
        {
            if (string.IsNullOrWhiteSpace(industry))
            {
                continue;
            }

            var direct = haystack.Contains(industry, StringComparison.OrdinalIgnoreCase);
            var synonym = IndustrySynonymCatalog.MatchIndustry(haystack, [industry]);
            if (!direct && string.IsNullOrWhiteSpace(synonym))
            {
                continue;
            }

            var label = direct ? industry.Trim() : (synonym ?? industry.Trim());
            if (label.Length > bestLength)
            {
                best = industry.Trim();
                bestLength = label.Length;
            }
        }

        return best;
    }
}
