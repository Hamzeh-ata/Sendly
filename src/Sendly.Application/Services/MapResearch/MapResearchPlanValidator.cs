using Sendly.Application.Abstractions;

namespace Sendly.Application.Services.MapResearch;

public static class MapResearchPlanValidator
{
    public static void Validate(MapResearchPlan plan)
    {
        plan.ValidationIssues = [];
        var spec = plan.Specification;

        if (!spec.IsValid)
        {
            AddIssue(plan, "spec.invalid", "Project research specification is invalid.", "error");
        }

        if (spec.ApprovedIndustries.Count == 0 && plan.KeywordFamilies.Count == 0)
        {
            AddIssue(plan, "spec.no_targets", "No industries or keyword families were available for map research.", "error");
        }

        if (spec.TargetCountries.Count == 0)
        {
            AddIssue(plan, "spec.no_countries", "Map research requires at least one target country.", "error");
        }

        if (GeographicBoundsCatalog.ResolveMany(spec.TargetCountries).Count == 0)
        {
            AddIssue(plan, "geo.unresolved", "None of the target countries could be resolved to geographic bounds.", "error");
        }

        ValidateMandatoryConcepts(plan);
        ValidateExclusions(plan);
        ValidateGeography(plan);
        ValidateBudgets(plan);
        ValidateUnsupported(plan);

        plan.IsValid = plan.ValidationIssues.All(x =>
            !string.Equals(x.Severity, "error", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateMandatoryConcepts(MapResearchPlan plan)
    {
        foreach (var family in plan.KeywordFamilies.Where(x => x.IsMandatory))
        {
            var hasMapping = plan.SourceCategoryMappings.Any(x =>
                x.IsSupported && string.Equals(x.FamilyKey, family.FamilyKey, StringComparison.OrdinalIgnoreCase));
            var hasNameFallback = family.Terms.Any(x =>
                x.SearchUse is KeywordSearchUses.PrimaryDiscovery or KeywordSearchUses.AlternateDiscovery);
            if (!hasMapping && !hasNameFallback)
            {
                AddIssue(
                    plan,
                    "plan.mandatory_dropped",
                    $"Mandatory concept '{family.BusinessConcept}' has no supported source category or searchable terms.",
                    "error");
            }
        }
    }

    private static void ValidateExclusions(MapResearchPlan plan)
    {
        var exclusions = plan.Specification.ExcludedIndustries
            .Concat(plan.Specification.ExcludedKeywords)
            .Concat(plan.Specification.ExcludedEntityTypes)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        foreach (var mapping in plan.SourceCategoryMappings.Where(x => x.IsSupported))
        {
            foreach (var exclusion in exclusions)
            {
                if (mapping.SourceCategory.Contains(exclusion, StringComparison.OrdinalIgnoreCase)
                    || mapping.OriginatingRequirement.Contains(exclusion, StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue(
                        plan,
                        "plan.exclusion_violation",
                        $"Source category '{mapping.SourceCategory}' conflicts with exclusion '{exclusion}'.",
                        "error");
                }
            }
        }

        foreach (var family in plan.KeywordFamilies)
        {
            foreach (var term in family.Terms)
            {
                foreach (var exclusion in exclusions)
                {
                    if (string.Equals(term.Term, exclusion, StringComparison.OrdinalIgnoreCase))
                    {
                        AddIssue(
                            plan,
                            "plan.excluded_term",
                            $"Keyword family term '{term.Term}' is explicitly excluded by the project.",
                            "error");
                    }
                }
            }
        }
    }

    private static void ValidateGeography(MapResearchPlan plan)
    {
        var countries = GeographicBoundsCatalog.ResolveMany(plan.Specification.TargetCountries);
        foreach (var cell in plan.GeographicCells)
        {
            if (cell.South >= cell.North || cell.West >= cell.East)
            {
                AddIssue(plan, "geo.invalid_bbox", $"Invalid bounding box for region '{cell.RegionLabel}'.", "error");
                continue;
            }

            var country = countries.FirstOrDefault(x =>
                string.Equals(x.CountryCode, cell.CountryCode, StringComparison.OrdinalIgnoreCase));
            if (country is null)
            {
                AddIssue(plan, "geo.unknown_country", $"Cell '{cell.RegionLabel}' references an unresolved country.", "error");
                continue;
            }

            if (cell.South < country.South - 0.5
                || cell.North > country.North + 0.5
                || cell.West < country.West - 0.5
                || cell.East > country.East + 0.5)
            {
                AddIssue(
                    plan,
                    "geo.out_of_bounds",
                    $"Cell '{cell.RegionLabel}' extends outside the configured country bounds.",
                    "error");
            }
        }
    }

    private static void ValidateBudgets(MapResearchPlan plan)
    {
        if (plan.GeographicCells.Count > plan.MaxOverpassRequests)
        {
            AddIssue(
                plan,
                "budget.cells",
                $"Plan has {plan.GeographicCells.Count} geographic queries which exceeds the configured Overpass budget of {plan.MaxOverpassRequests}.",
                "warning");
            plan.GeographicCells = plan.GeographicCells.Take(plan.MaxOverpassRequests).ToList();
            plan.GenerationLog.Add($"Trimmed geographic cells to Overpass budget ({plan.MaxOverpassRequests}).");
        }

        if (plan.MaxRecordsPerRun <= 0)
        {
            AddIssue(plan, "budget.records", "Max records per run must be greater than zero.", "error");
        }
    }

    private static void ValidateUnsupported(MapResearchPlan plan)
    {
        if (plan.SourceCategoryMappings.All(x => !x.IsSupported) && plan.KeywordFamilies.Count > 0)
        {
            AddIssue(
                plan,
                "source.unsupported",
                "No supported OpenStreetMap categories were mapped for the project concepts. Name-based Overpass filters may still be used when terms exist.",
                "warning");
        }

        foreach (var unsupported in plan.UnsupportedCategories)
        {
            AddIssue(
                plan,
                "source.coverage_gap",
                $"Unsupported or unmapped concept: {unsupported}",
                "warning");
        }
    }

    private static void AddIssue(MapResearchPlan plan, string code, string message, string severity)
    {
        plan.ValidationIssues.Add(new MapResearchPlanValidationIssue
        {
            Code = code,
            Message = message,
            Severity = severity
        });
    }
}
