using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;

namespace Sendly.Application.Services.MapResearch;

public class MapResearchPlanBuilder
{
    private readonly IMapResearchIntelligence? _intelligence;

    public MapResearchPlanBuilder(IMapResearchIntelligence? intelligence = null)
    {
        _intelligence = intelligence;
    }

    public async Task<MapResearchPlan> BuildAsync(
        OutreachProject project,
        ResearchSpecification specification,
        ResearchBudgetSpec budgets,
        CancellationToken cancellationToken)
    {
        var plan = new MapResearchPlan
        {
            Specification = specification,
            MaxOverpassRequests = Math.Clamp(Math.Min(budgets.MaxSearchRequests, 24), 4, 24),
            MaxRecordsPerRun = Math.Clamp(budgets.MaxUrlsPerRun, 10, 500)
        };

        var keywordRequest = BuildKeywordRequest(project, specification);
        plan.GenerationLog.Add(
            $"Normalized project '{project.Name}' with {specification.ApprovedIndustries.Count} industries and {specification.TargetCountries.Count} countries.");

        var deterministicFamilies = MapKeywordFamilyBuilder.BuildDeterministic(keywordRequest);
        plan.KeywordFamilies = deterministicFamilies;
        plan.GenerationLog.Add($"Built {deterministicFamilies.Count} deterministic keyword families from project configuration.");

        if (_intelligence is not null)
        {
            await MergeAiKeywordFamiliesAsync(plan, keywordRequest, cancellationToken);
        }

        var mappingRequest = new MapSourceCategoryMappingRequest
        {
            Specification = specification,
            KeywordFamilies = plan.KeywordFamilies,
            AvailableTags = OsmTagCapabilityCatalog.All.ToList()
        };
        var deterministicMappings = OsmSourceCategoryMapper.MapDeterministic(mappingRequest);
        plan.SourceCategoryMappings = deterministicMappings.Mappings;
        plan.UnsupportedCategories = deterministicMappings.UnsupportedConcepts;
        plan.GenerationLog.Add(
            $"Mapped {deterministicMappings.Mappings.Count(x => x.IsSupported)} supported OSM categories; unsupported concepts={deterministicMappings.UnsupportedConcepts.Count}.");

        if (_intelligence is not null)
        {
            await MergeAiCategoryMappingsAsync(plan, mappingRequest, cancellationToken);
        }

        RejectUnrelatedMappings(plan);
        plan.GeographicCells = GeographicCoveragePlanner.BuildInitialCells(
            specification,
            plan.KeywordFamilies,
            plan.SourceCategoryMappings,
            plan.MaxOverpassRequests);
        plan.GenerationLog.Add($"Created {plan.GeographicCells.Count} geographic cells for resumable Overpass execution.");

        foreach (var mapping in plan.SourceCategoryMappings.Where(x => x.IsSupported))
        {
            plan.GenerationLog.Add(
                $"Query category '{mapping.SourceCategory}' serves requirement '{mapping.OriginatingRequirement}' ({mapping.MatchKind}).");
        }

        MapResearchPlanValidator.Validate(plan);
        return plan;
    }

    private static MapKeywordFamilyGenerationRequest BuildKeywordRequest(
        OutreachProject project,
        ResearchSpecification specification)
    {
        var mandatory = new List<string>();
        var optional = new List<string>();
        var allKeywords = new List<string>();

        foreach (var keyword in project.Keywords)
        {
            if (keyword.IsExcluded)
            {
                continue;
            }

            allKeywords.Add(keyword.Value);
            if (specification.ApprovedIndustries.Any(x =>
                    string.Equals(x, keyword.Value, StringComparison.OrdinalIgnoreCase)))
            {
                mandatory.Add(keyword.Value);
            }
            else
            {
                optional.Add(keyword.Value);
            }
        }

        return new MapKeywordFamilyGenerationRequest
        {
            Specification = specification,
            ProjectKeywords = allKeywords,
            MandatoryKeywords = mandatory,
            OptionalKeywords = optional
        };
    }

    private async Task MergeAiKeywordFamiliesAsync(
        MapResearchPlan plan,
        MapKeywordFamilyGenerationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var ai = await _intelligence!.GenerateKeywordFamiliesAsync(request, cancellationToken);
            if (!ai.IsValid)
            {
                plan.GenerationLog.Add($"Ollama keyword-family generation skipped: {ai.ErrorSummary}");
                return;
            }

            plan.KeywordFamilies = MapKeywordFamilyBuilder.MergeAiFamilies(
                plan.KeywordFamilies,
                ai.Families,
                plan.Specification);
            plan.GenerationLog.Add($"Merged Ollama keyword families; total families={plan.KeywordFamilies.Count}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            plan.GenerationLog.Add($"Ollama keyword-family generation failed: {ex.Message}");
        }
    }

    private async Task MergeAiCategoryMappingsAsync(
        MapResearchPlan plan,
        MapSourceCategoryMappingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            request.KeywordFamilies = plan.KeywordFamilies;
            var ai = await _intelligence!.MapSourceCategoriesAsync(request, cancellationToken);
            if (!ai.IsValid)
            {
                plan.GenerationLog.Add($"Ollama category mapping skipped: {ai.ErrorSummary}");
                return;
            }

            var merged = OsmSourceCategoryMapper.MergeAiMappings(
                new MapSourceCategoryMappingResult
                {
                    Mappings = plan.SourceCategoryMappings,
                    UnsupportedConcepts = plan.UnsupportedCategories,
                    IsValid = true
                },
                ai,
                plan.Specification,
                OsmTagCapabilityCatalog.All);
            plan.SourceCategoryMappings = merged.Mappings;
            plan.UnsupportedCategories = merged.UnsupportedConcepts;
            plan.GenerationLog.Add($"Merged Ollama OSM category mappings; supported={merged.Mappings.Count(x => x.IsSupported)}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            plan.GenerationLog.Add($"Ollama category mapping failed: {ex.Message}");
        }
    }

    private static void RejectUnrelatedMappings(MapResearchPlan plan)
    {
        var kept = new List<MapSourceCategoryMapping>();
        foreach (var mapping in plan.SourceCategoryMappings)
        {
            if (!mapping.IsSupported)
            {
                kept.Add(mapping);
                continue;
            }

            if (IntroducesUnrelatedIndustry(mapping, plan.Specification))
            {
                plan.GenerationLog.Add(
                    $"Rejected unrelated mapping '{mapping.SourceCategory}' for '{mapping.OriginatingRequirement}'.");
                continue;
            }

            kept.Add(mapping);
        }

        plan.SourceCategoryMappings = kept;
    }

    private static bool IntroducesUnrelatedIndustry(MapSourceCategoryMapping mapping, ResearchSpecification spec)
    {
        if (spec.ApprovedIndustries.Count == 0)
        {
            return false;
        }

        var family = mapping.OriginatingRequirement;
        foreach (var industry in spec.ApprovedIndustries)
        {
            if (family.Contains(industry, StringComparison.OrdinalIgnoreCase)
                || industry.Contains(family, StringComparison.OrdinalIgnoreCase)
                || mapping.RelevanceReason.Contains(industry, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return mapping.MatchKind == SourceCategoryMatchKinds.Exploratory;
    }
}
