using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;

namespace Sendly.Application.Services;

public static class DiscoveryQueryPlanner
{
    public static DiscoveryPlan Build(
        ResearchQueryVersion? version,
        ProspectSourceContext context,
        DiscoveryBudget budget,
        IReadOnlyList<string> availableProviders)
    {
        var industries = CollectIndustries(version, context);
        var synonyms = IndustrySynonymCatalog.Expand(industries);
        var countries = CollectCountries(version, context);
        var characteristics = CollectJsonList(version?.CompanyTypesJson);
        var mandatory = CollectMandatory(version, context);
        var stages = BuildStages(budget.MaxDiscoveryStages, context);

        var plan = new DiscoveryPlan
        {
            TargetIndustries = industries,
            IndustrySynonyms = synonyms.Where(x => !industries.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList(),
            TargetCountries = countries,
            CompanyCharacteristics = characteristics,
            MandatoryCriteria = mandatory,
            ProviderStages = stages,
            EligibleProviderNames = availableProviders.ToList(),
            MaxCandidateDomains = budget.MaxCandidateDomains,
            MaxUrls = budget.MaxUrlsPerRun,
            MaxPagesPerDomain = budget.MaxPagesPerDomain,
            MaxCrawlDepth = budget.MaxCrawlDepth,
            MaxSourceExpansionDepth = budget.MaxSourceExpansionDepth,
            MaxArchiveRecords = budget.MaxArchiveRecords,
            MaxDiscoveryStages = budget.MaxDiscoveryStages,
            MinCandidatesBeforeRecovery = Math.Max(5, budget.MaxUrlsPerRun / 10)
        };

        plan.Summary =
            $"Plan: {plan.TargetIndustries.Count} industries (+{plan.IndustrySynonyms.Count} synonyms), "
            + $"{plan.TargetCountries.Count} countries, {plan.EligibleProviderNames.Count} providers, "
            + $"budget {plan.MaxUrls} URLs / {plan.MaxCandidateDomains} domains / {plan.MaxDiscoveryStages} stages.";

        return plan;
    }

    private static List<string> CollectIndustries(ResearchQueryVersion? version, ProspectSourceContext context)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddUnique(result, seen, version?.TargetIndustry);
        foreach (var item in CollectJsonList(version?.BusinessCategoriesJson))
        {
            AddUnique(result, seen, item);
        }

        foreach (var item in context.Industries)
        {
            AddUnique(result, seen, item);
        }

        return result;
    }

    private static List<string> CollectCountries(ResearchQueryVersion? version, ProspectSourceContext context)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddUnique(result, seen, version?.TargetCountry);
        foreach (var item in CollectJsonList(version?.RegionsJson))
        {
            AddUnique(result, seen, item);
        }

        foreach (var item in context.Countries)
        {
            AddUnique(result, seen, item);
        }

        return result;
    }

    private static List<string> CollectMandatory(ResearchQueryVersion? version, ProspectSourceContext context)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in CollectJsonList(version?.RequiredCriteriaJson))
        {
            AddUnique(result, seen, item);
        }

        if (version?.RequireOfficialWebsite == true)
        {
            AddUnique(result, seen, "Official website");
        }

        if (version?.RequirePublicBusinessEmail == true || context.IncludeEmail)
        {
            AddUnique(result, seen, "Public business email");
        }

        return result;
    }

    private static List<string> BuildStages(int maxStages, ProspectSourceContext context)
    {
        var stages = new List<string>
        {
            "Primary registry and prior research",
            "Synonym expansion and secondary providers",
            "Archive and recovery providers"
        };

        return stages.Take(Math.Clamp(maxStages, 1, stages.Count)).ToList();
    }

    private static List<string> CollectJsonList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)
                       ?.Where(x => !string.IsNullOrWhiteSpace(x))
                       .Select(x => x.Trim())
                       .ToList()
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AddUnique(List<string> result, HashSet<string> seen, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (seen.Add(trimmed))
        {
            result.Add(trimmed);
        }
    }
}
