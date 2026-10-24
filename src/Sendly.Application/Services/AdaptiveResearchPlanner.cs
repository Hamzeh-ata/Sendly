using Microsoft.Extensions.Logging;
using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public class AdaptiveResearchPlanner
{
    private readonly ILocalAiResearchIntelligence? _intelligence;
    private readonly ILogger<AdaptiveResearchPlanner> _logger;

    public AdaptiveResearchPlanner(
        ILogger<AdaptiveResearchPlanner> logger,
        ILocalAiResearchIntelligence? intelligence = null)
    {
        _logger = logger;
        _intelligence = intelligence;
    }

    public async Task<IReadOnlyList<PlannedSearchQuery>> PlanAsync(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        IReadOnlyList<ResearchSourceCatalogEntry> availableSources,
        int maxActions,
        bool isFollowUp,
        int remainingOllamaCalls,
        int remainingSearchRequests,
        CancellationToken cancellationToken)
    {
        if (_intelligence is null || remainingOllamaCalls <= 0 || remainingSearchRequests <= 0)
        {
            return [];
        }

        var spec = ProjectEligibilityPolicy.RequireSpec(context);
        if (!spec.IsValid || spec.ApprovedIndustries.Count == 0 || spec.TargetCountries.Count == 0)
        {
            return [];
        }

        var request = BuildPlanRequest(
            context,
            state,
            spec,
            availableSources,
            maxActions,
            isFollowUp,
            remainingOllamaCalls,
            remainingSearchRequests);

        ResearchPlanResult plan;
        try
        {
            plan = await _intelligence.ProposeResearchPlanAsync(request, cancellationToken);
            state.OllamaCallsUsed++;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Adaptive research plan request failed; deterministic planning continues");
            state.RecordRejection("AdaptivePlanFailed");
            return [];
        }

        if (!plan.IsValid)
        {
            state.RecordRejection("AdaptivePlanInvalid");
            return [];
        }

        MergeAbandonedPatterns(state, plan.AbandonedPatterns);
        var accepted = ValidateAndAcceptActions(plan.Actions, spec, availableSources, state, maxActions);
        RecordDecisions(state, accepted, plan.StrategySummary);
        return ConvertToPlannedQueries(accepted, context, spec);
    }

    public static IReadOnlyList<ResearchSourceCatalogEntry> BuildDefaultSourceCatalog(
        bool webSearchAvailable,
        bool independentCrawlerAvailable,
        bool directoryCatalogAvailable,
        bool associationCatalogAvailable,
        bool osmAvailable,
        bool localIndexAvailable,
        bool commonCrawlAvailable)
    {
        var catalog = new List<ResearchSourceCatalogEntry>();
        AddSource(catalog, "WebSearch", "search", webSearchAvailable, true, true, false, "HTML and configured search providers");
        AddSource(catalog, "IndependentCrawler", "crawler", independentCrawlerAvailable, false, false, false, "HTTP crawl of admitted URLs");
        AddSource(catalog, "DirectoryCatalog", "directory", directoryCatalogAvailable, true, true, false, "Specialist directory listings");
        AddSource(catalog, "IndustryAssociationCatalog", "association", associationCatalogAvailable, true, true, false, "Industry association listings");
        AddSource(catalog, "OpenStreetMapNominatim", "geo", osmAvailable, true, true, false, "Geographic venue discovery");
        AddSource(catalog, "LocalBusinessIndex", "index", localIndexAvailable, true, true, false, "Persistent local business index");
        AddSource(catalog, "CommonCrawlIndex", "archive", commonCrawlAvailable, false, false, false, "Archive URL discovery");
        AddSource(catalog, "PriorResearchUrls", "history", true, false, false, false, "URLs from prior research runs");
        AddSource(catalog, "SeedUrls", "seed", true, false, false, false, "Project seed URLs");
        return catalog;
    }

    public static ResearchPlanRequest BuildPlanRequest(
        ProspectSourceContext context,
        ResearchDiscoveryState state,
        ResearchSpecification spec,
        IReadOnlyList<ResearchSourceCatalogEntry> availableSources,
        int maxActions,
        bool isFollowUp,
        int remainingOllamaCalls,
        int remainingSearchRequests)
    {
        return new ResearchPlanRequest
        {
            ProjectName = context.ProjectName,
            ProjectDescription = context.Description,
            ResearchInstructions = context.ResearchInstructions,
            Specification = spec,
            ExistingQueries = state.GeneratedQueries.Take(40).ToList(),
            AbandonedPatterns = state.AbandonedSearchPatterns.Take(20).ToList(),
            AvailableSources = availableSources.Where(x => x.IsAvailable && !x.IsTemporarilyDisabled).ToList(),
            OutcomeSummary = ResearchIntelligenceMetrics.BuildCompactOutcomeSummary(state, spec),
            BudgetSummary = ResearchIntelligenceMetrics.BuildBudgetSummary(state, remainingOllamaCalls, remainingSearchRequests),
            CoverageSummary =
                $"qualified={state.ProspectsSaved}/{spec.TargetProspectCount}; industries={string.Join('|', spec.ApprovedIndustries)}; countries={string.Join('|', spec.TargetCountries)}",
            RejectionSummary = state.RejectionReasons.Count == 0
                ? null
                : string.Join(", ", state.RejectionReasons.Select(x => $"{x.Key}={x.Value}").Take(20)),
            UnderCoveredIndustries = state.ResolveUnderCoveredIndustries(),
            UnderCoveredLocations = ResearchIntelligenceMetrics.ResolveUnderCoveredLocations(state, spec),
            MaxActions = Math.Clamp(maxActions, 1, 12),
            IsFollowUp = isFollowUp
        };
    }

    public static List<ResearchActionProposal> ValidateAndAcceptActions(
        IEnumerable<ResearchActionProposal> proposals,
        ResearchSpecification spec,
        IReadOnlyList<ResearchSourceCatalogEntry> availableSources,
        ResearchDiscoveryState state,
        int maxActions)
    {
        var accepted = new List<ResearchActionProposal>();
        var seenQueries = new HashSet<string>(state.GeneratedQueries, StringComparer.OrdinalIgnoreCase);
        var availableNames = new HashSet<string>(
            availableSources.Where(x => x.IsAvailable && !x.IsTemporarilyDisabled).Select(x => x.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var proposal in proposals)
        {
            if (accepted.Count >= maxActions)
            {
                break;
            }

            var validated = ValidateAction(proposal, spec, availableNames, seenQueries, state);
            if (!validated.IsAccepted)
            {
                state.AdaptiveActionsRejected++;
                state.RecordRejection(validated.RejectionReason ?? "AdaptiveActionRejected");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(validated.Query))
            {
                seenQueries.Add(validated.Query);
            }

            state.AdaptiveActionsAccepted++;
            accepted.Add(validated);
        }

        return accepted;
    }

    public static ResearchActionProposal ValidateAction(
        ResearchActionProposal proposal,
        ResearchSpecification spec,
        HashSet<string> availableSourceNames,
        HashSet<string> seenQueries,
        ResearchDiscoveryState state)
    {
        var result = CloneProposal(proposal);
        if (string.IsNullOrWhiteSpace(result.ActionType)
            || !ResearchActionTypes.Allowed.Contains(result.ActionType.Trim()))
        {
            result.IsAccepted = false;
            result.RejectionReason = "InvalidActionType";
            return result;
        }

        result.ActionType = result.ActionType.Trim();
        if (!string.IsNullOrWhiteSpace(result.PreferredSource)
            && !availableSourceNames.Contains(result.PreferredSource.Trim()))
        {
            result.PreferredSource = null;
        }

        if (LooksLikeAbandonedPattern(result, state))
        {
            result.IsAccepted = false;
            result.RejectionReason = "AbandonedPattern";
            return result;
        }

        if (string.Equals(result.ActionType, ResearchActionTypes.InvestigateDiscoveredDomain, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsSafeHttpUrl(result.TargetDomainOrUrl) && string.IsNullOrWhiteSpace(result.Query))
            {
                result.IsAccepted = false;
                result.RejectionReason = "MissingInvestigationTarget";
                return result;
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Query) || RequiresSearchQuery(result.ActionType))
        {
            var query = result.Query?.Trim();
            if (string.IsNullOrWhiteSpace(query) || query.Length > 200 || !seenQueries.Add(query))
            {
                result.IsAccepted = false;
                result.RejectionReason = "InvalidOrDuplicateQuery";
                return result;
            }

            var industry = MatchApprovedIndustry(result.TargetIndustry, spec) ?? MatchApprovedIndustry(query, spec);
            var country = MatchApprovedCountry(result.Geography, spec) ?? MatchApprovedCountry(query, spec);
            var validation = ProjectEligibilityPolicy.ValidateQuery(query, spec, industry, country);
            if (!validation.IsValid)
            {
                result.IsAccepted = false;
                result.RejectionReason = validation.ReasonCode;
                return result;
            }

            result.Query = query;
            result.TargetIndustry = validation.MatchedIndustry ?? industry;
            result.Geography = validation.MatchedCountry ?? country ?? result.Geography;
        }

        result.IsAccepted = true;
        result.RejectionReason = null;
        result.AddressesRequirements = BuildRequirementTags(result, spec);
        result.Priority = Math.Clamp(result.Priority <= 0 ? 50 : result.Priority, 1, 100);
        return result;
    }

    public static List<PlannedSearchQuery> ConvertToPlannedQueries(
        IEnumerable<ResearchActionProposal> actions,
        ProspectSourceContext context,
        ResearchSpecification spec)
    {
        var planned = new List<PlannedSearchQuery>();
        foreach (var action in actions)
        {
            if (!action.IsAccepted || string.IsNullOrWhiteSpace(action.Query))
            {
                continue;
            }

            planned.Add(new PlannedSearchQuery
            {
                Query = action.Query.Trim(),
                Industry = action.TargetIndustry
                           ?? spec.ApprovedIndustries.FirstOrDefault()
                           ?? context.Industries.FirstOrDefault()
                           ?? string.Empty,
                Country = ExtractCountry(action.Geography, spec)
                          ?? spec.TargetCountries.FirstOrDefault()
                          ?? context.Countries.FirstOrDefault(),
                Location = ExtractLocation(action.Geography, spec)
                           ?? spec.Cities.FirstOrDefault()
                           ?? context.Locations.FirstOrDefault(),
                Intent = MapIntent(action.ActionType),
                Language = InferLanguage(action.ActionType, spec),
                ProjectId = context.ProjectId,
                ResearchQueryVersionId = context.QueryVersion?.Id,
                Purpose = string.IsNullOrWhiteSpace(action.Purpose) ? action.ActionType : action.Purpose,
                ExpectedResultType = string.IsNullOrWhiteSpace(action.ExpectedResult)
                    ? "operating_business"
                    : action.ExpectedResult,
                Provider = action.PreferredSource,
                ExecutionStatus = "planned",
                Keywords = action.AddressesRequirements
            });
        }

        return planned;
    }

    private static void RecordDecisions(
        ResearchDiscoveryState state,
        IReadOnlyList<ResearchActionProposal> accepted,
        string strategySummary)
    {
        if (!string.IsNullOrWhiteSpace(strategySummary))
        {
            state.LastAdaptiveStrategySummary = strategySummary.Trim();
        }

        foreach (var action in accepted)
        {
            state.IntelligenceDecisions.Add(new ResearchIntelligenceDecisionRecord
            {
                ActionType = action.ActionType,
                Query = action.Query,
                Reason = action.Reason,
                AddressesRequirements = action.AddressesRequirements.ToList(),
                EvidenceSummary = state.LastAdaptiveStrategySummary,
                Outcome = "planned",
                WasUseful = false,
                NextPlanChange = strategySummary
            });
        }
    }

    private static void MergeAbandonedPatterns(ResearchDiscoveryState state, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            var trimmed = pattern?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 120)
            {
                continue;
            }

            if (!state.AbandonedSearchPatterns.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                state.AbandonedSearchPatterns.Add(trimmed);
            }
        }
    }

    private static bool LooksLikeAbandonedPattern(ResearchActionProposal action, ResearchDiscoveryState state)
    {
        var text = $"{action.Query} {action.Purpose} {action.Reason}".Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var pattern in state.AbandonedSearchPatterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern)
                && text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(action.Query))
        {
            var yieldKey = action.Query.Trim().ToLowerInvariant();
            foreach (var lowYield in state.LowYieldQueryKeys)
            {
                if (lowYield.Contains(yieldKey, StringComparison.OrdinalIgnoreCase)
                    || yieldKey.Contains(lowYield, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool RequiresSearchQuery(string actionType)
        => !string.Equals(actionType, ResearchActionTypes.InvestigateDiscoveredDomain, StringComparison.OrdinalIgnoreCase);

    private static string MapIntent(string actionType)
        => actionType switch
        {
            ResearchActionTypes.DirectoryListingSearch => SearchQueryIntents.Directory,
            ResearchActionTypes.AssociationListingSearch => SearchQueryIntents.Association,
            ResearchActionTypes.BusinessNameWebsiteSearch => SearchQueryIntents.OfficialWebsite,
            ResearchActionTypes.MissingContactSearch => SearchQueryIntents.ContactPage,
            ResearchActionTypes.OfficialRegistrySearch => SearchQueryIntents.RegionalListing,
            ResearchActionTypes.CoverageGapSearch => SearchQueryIntents.DiscoverBusiness,
            _ => SearchQueryIntents.DiscoverBusiness
        };

    private static string InferLanguage(string actionType, ResearchSpecification spec)
    {
        if (string.Equals(actionType, ResearchActionTypes.LocalLanguageSearch, StringComparison.OrdinalIgnoreCase)
            && spec.LanguageHints.Count > 0)
        {
            return spec.LanguageHints[0];
        }

        return "en";
    }

    private static string? MatchApprovedIndustry(string? value, ResearchSpecification spec)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var industry in spec.ApprovedIndustries)
        {
            if (value.Contains(industry, StringComparison.OrdinalIgnoreCase)
                || industry.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return industry;
            }
        }

        foreach (var synonym in spec.IndustrySynonyms)
        {
            if (value.Contains(synonym, StringComparison.OrdinalIgnoreCase))
            {
                return spec.ApprovedIndustries.FirstOrDefault();
            }
        }

        return null;
    }

    private static string? MatchApprovedCountry(string? value, ResearchSpecification spec)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var country in spec.TargetCountries)
        {
            if (value.Contains(country, StringComparison.OrdinalIgnoreCase)
                || country.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return country;
            }
        }

        foreach (var alias in spec.GeographicAliases)
        {
            if (value.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                return spec.TargetCountries.FirstOrDefault();
            }
        }

        return null;
    }

    private static string? ExtractCountry(string? geography, ResearchSpecification spec)
        => MatchApprovedCountry(geography, spec);

    private static string? ExtractLocation(string? geography, ResearchSpecification spec)
    {
        if (string.IsNullOrWhiteSpace(geography))
        {
            return null;
        }

        foreach (var city in spec.Cities)
        {
            if (geography.Contains(city, StringComparison.OrdinalIgnoreCase))
            {
                return city;
            }
        }

        return geography.Trim();
    }

    private static List<string> BuildRequirementTags(ResearchActionProposal action, ResearchSpecification spec)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(action.TargetIndustry))
        {
            tags.Add($"industry:{action.TargetIndustry}");
        }

        if (!string.IsNullOrWhiteSpace(action.Geography))
        {
            tags.Add($"geo:{action.Geography}");
        }

        tags.Add($"action:{action.ActionType}");
        if (spec.RequireOfficialWebsite)
        {
            tags.Add("require:official_website");
        }

        return tags;
    }

    private static bool IsSafeHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static ResearchActionProposal CloneProposal(ResearchActionProposal proposal)
        => new()
        {
            ActionType = proposal.ActionType,
            Query = proposal.Query,
            Purpose = proposal.Purpose,
            TargetIndustry = proposal.TargetIndustry,
            Geography = proposal.Geography,
            ExpectedResult = proposal.ExpectedResult,
            Reason = proposal.Reason,
            PreferredSource = proposal.PreferredSource,
            TargetDomainOrUrl = proposal.TargetDomainOrUrl,
            Priority = proposal.Priority,
            AddressesRequirements = proposal.AddressesRequirements.ToList(),
            IsAccepted = proposal.IsAccepted,
            RejectionReason = proposal.RejectionReason
        };

    private static void AddSource(
        List<ResearchSourceCatalogEntry> catalog,
        string name,
        string kind,
        bool available,
        bool industry,
        bool country,
        bool paid,
        string notes)
    {
        catalog.Add(new ResearchSourceCatalogEntry
        {
            Name = name,
            SourceKind = kind,
            IsAvailable = available,
            SupportsIndustryFilter = industry,
            SupportsCountryFilter = country,
            RequiresPaidApi = paid,
            AccessNotes = notes,
            Health = available ? SearchProviderHealthState.Healthy.ToString() : SearchProviderHealthState.Unavailable.ToString()
        });
    }
}
