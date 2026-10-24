using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public class AdaptiveRecoveryAction
{
    public string Reason { get; set; } = string.Empty;

    public string Strategy { get; set; } = string.Empty;

    public bool ExpandIndustrySynonyms { get; set; }

    public bool ExpandGeographicTerms { get; set; }

    public bool PreferUnexploredProviders { get; set; }

    public bool DeprioritizeDeadDomains { get; set; }

    public bool EnableContactPageHints { get; set; }

    public bool TightenUrlFiltering { get; set; }

    public bool PreferOfficialCompanyDomains { get; set; }

    public bool PreferFreshSources { get; set; }

    public string? TriggerSignal { get; set; }
}

public static class AdaptiveDiscoveryRecovery
{
    private static readonly Dictionary<string, string[]> CountryVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Saudi Arabia"] = ["KSA", "SA", "Kingdom of Saudi Arabia", "السعودية", "المملكة العربية السعودية"],
        ["United Arab Emirates"] = ["UAE", "AE", "Dubai", "Abu Dhabi", "الإمارات"],
        ["Egypt"] = ["EG", "مصر"],
        ["Jordan"] = ["JO", "الأردن"],
        ["United Kingdom"] = ["UK", "GB", "Britain"],
        ["United States"] = ["USA", "US", "America"],
        ["New Zealand"] = ["NZ", "Aotearoa"]
    };

    public static AdaptiveRecoveryAction? Decide(
        UrlDiscoveryContext context,
        AutomaticDiscoveryResult current,
        int duplicateRatePercent,
        int failedFetchPercent)
    {
        var volumeSignals = new DiscoveryYieldSignals
        {
            UrlsDiscovered = current.Candidates.Count,
            Duplicates = current.DuplicatesRemoved,
            CandidatesProcessed = current.Candidates.Count + current.DuplicatesRemoved,
            SourceFailures = current.ProviderStats.Count(x => x.Attempted && !x.Succeeded),
            SourcesAttempted = current.ProviderStats.Count(x => x.Attempted),
            FetchesAttempted = failedFetchPercent > 0 ? 100 : 0,
            HtmlFetchesSucceeded = failedFetchPercent > 0 ? Math.Max(0, 100 - failedFetchPercent) : 0,
            PriorSourceUrls = current.Candidates.Count(x => x.SourceType == DiscoverySourceType.PriorResearch),
            FreshSourceUrls = current.Candidates.Count(x => x.SourceType != DiscoverySourceType.PriorResearch),
            AssetRejections = current.RejectedFiltered,
            RequestedProspects = context.MinCandidatesBeforeRecovery,
            QualifiedProspects = current.Candidates.Count >= context.MinCandidatesBeforeRecovery
                ? context.MinCandidatesBeforeRecovery
                : 0
        };

        return DecideFromOutcomes(context, current, volumeSignals, duplicateRatePercent, failedFetchPercent);
    }

    public static AdaptiveRecoveryAction? DecideFromOutcomes(
        UrlDiscoveryContext context,
        AutomaticDiscoveryResult current,
        DiscoveryYieldSignals yield,
        int duplicateRatePercent,
        int failedFetchPercent)
    {
        if (current.ProviderStats.Count(x => x.Attempted && !x.Succeeded)
            == current.ProviderStats.Count(x => x.Attempted)
            && current.ProviderStats.Any(x => x.Attempted))
        {
            return new AdaptiveRecoveryAction
            {
                Reason = "All attempted discovery providers failed.",
                Strategy = "ContinueWithHealthyProviders",
                PreferUnexploredProviders = true,
                ExpandIndustrySynonyms = true,
                TriggerSignal = "source_failure_rate"
            };
        }

        var effectiveFailedFetch = Math.Max(failedFetchPercent, (int)Math.Round(100 - yield.HtmlFetchSuccessPercent));
        if (effectiveFailedFetch >= 50 && yield.FetchesAttempted >= 5)
        {
            return new AdaptiveRecoveryAction
            {
                Reason = "High rate of inaccessible candidate domains.",
                Strategy = "DeprioritizeDeadDomains",
                DeprioritizeDeadDomains = true,
                PreferUnexploredProviders = true,
                ExpandGeographicTerms = true,
                TriggerSignal = "fetch_failure_rate"
            };
        }

        if (yield.AssetRejectionPercent >= 25 || (yield.AssetRejections >= 20 && yield.UrlsDiscovered > 0))
        {
            return new AdaptiveRecoveryAction
            {
                Reason = "High asset/API URL pollution in discovery queue.",
                Strategy = "TightenUrlFiltering",
                TightenUrlFiltering = true,
                PreferOfficialCompanyDomains = true,
                PreferFreshSources = true,
                TriggerSignal = "asset_rejection_rate"
            };
        }

        if (yield.DirectoryEntities >= 3 || yield.IrrelevantEntityPercent >= 40)
        {
            return new AdaptiveRecoveryAction
            {
                Reason = "High directory/listicle entity rate relative to target businesses.",
                Strategy = "PreferOfficialCompanyDomains",
                PreferOfficialCompanyDomains = true,
                PreferUnexploredProviders = true,
                PreferFreshSources = true,
                EnableContactPageHints = true,
                TriggerSignal = "irrelevant_entity_rate"
            };
        }

        if (duplicateRatePercent >= 40 || yield.DuplicatePercent >= 40)
        {
            return new AdaptiveRecoveryAction
            {
                Reason = "High duplicate rate among discovered candidates.",
                Strategy = "PreferUnexploredSources",
                PreferUnexploredProviders = true,
                PreferFreshSources = true,
                ExpandIndustrySynonyms = true,
                TriggerSignal = "duplicate_rate"
            };
        }

        if (yield.FreshSourcePercent < 25 && yield.PriorSourceUrls >= Math.Max(5, context.MinCandidatesBeforeRecovery / 2))
        {
            return new AdaptiveRecoveryAction
            {
                Reason = "Prior research dominates discovery; fresh provider contribution is too low.",
                Strategy = "PreferFreshDiscoveryProviders",
                PreferUnexploredProviders = true,
                PreferFreshSources = true,
                ExpandIndustrySynonyms = true,
                TriggerSignal = "fresh_source_yield"
            };
        }

        if (yield.IsUnderperforming()
            && yield.AttributedEmails == 0
            && yield.UniqueTargetBusinesses > 0)
        {
            return new AdaptiveRecoveryAction
            {
                Reason = "Target businesses identified but attributed public email yield is zero.",
                Strategy = "PrioritizeOfficialContactPages",
                EnableContactPageHints = true,
                PreferOfficialCompanyDomains = true,
                TightenUrlFiltering = true,
                TriggerSignal = "email_yield"
            };
        }

        if (yield.IsUnderperforming()
            && yield.QualifiedProspects < Math.Max(1, context.MinCandidatesBeforeRecovery / 5))
        {
            return new AdaptiveRecoveryAction
            {
                Reason = "Qualified prospect yield is below recovery threshold despite candidate volume.",
                Strategy = "YieldQualityRecovery",
                ExpandIndustrySynonyms = true,
                PreferUnexploredProviders = true,
                PreferFreshSources = true,
                EnableContactPageHints = true,
                PreferOfficialCompanyDomains = true,
                TriggerSignal = "qualified_yield"
            };
        }

        if (current.Candidates.Count == 0)
        {
            return new AdaptiveRecoveryAction
            {
                Reason = "No candidate companies discovered in the current stage.",
                Strategy = "BroadenTerminology",
                ExpandIndustrySynonyms = true,
                ExpandGeographicTerms = true,
                PreferUnexploredProviders = true,
                TriggerSignal = "zero_candidates"
            };
        }

        if (current.Candidates.Count >= context.MinCandidatesBeforeRecovery
            && !yield.IsUnderperforming())
        {
            return null;
        }

        if (current.Candidates.Count < context.MinCandidatesBeforeRecovery)
        {
            return new AdaptiveRecoveryAction
            {
                Reason = "Candidate coverage below recovery threshold.",
                Strategy = "SynonymAndArchiveRecovery",
                ExpandIndustrySynonyms = true,
                PreferUnexploredProviders = true,
                EnableContactPageHints = true,
                TriggerSignal = "candidate_volume"
            };
        }

        return null;
    }

    public static void Apply(UrlDiscoveryContext context, AdaptiveRecoveryAction action)
    {
        if (action.ExpandIndustrySynonyms)
        {
            ExpandList(context.Industries, context.IndustrySynonyms);
        }

        if (action.ExpandGeographicTerms)
        {
            ExpandCountries(context.Countries);
        }

        if (action.PreferUnexploredProviders)
        {
            context.PreferUnexploredProviders = true;
        }

        if (action.DeprioritizeDeadDomains)
        {
            context.DeprioritizeDeadDomains = true;
        }

        if (action.EnableContactPageHints)
        {
            context.EnableContactPageHints = true;
        }

        if (action.TightenUrlFiltering)
        {
            context.TightenUrlFiltering = true;
        }

        if (action.PreferOfficialCompanyDomains)
        {
            context.PreferOfficialCompanyDomains = true;
        }

        if (action.PreferFreshSources)
        {
            context.PreferFreshSources = true;
            context.MaxPriorResearchSharePercent = Math.Min(context.MaxPriorResearchSharePercent, 20);
        }
    }

    public static IReadOnlyList<string> ContactPageHints()
        => ["contact", "about", "team", "location", "locations", "تواصل", "عنّا", "اتصل"];

    private static void ExpandCountries(List<string> countries)
    {
        var additions = new List<string>();
        foreach (var country in countries.ToList())
        {
            if (!CountryVariants.TryGetValue(country, out var variants))
            {
                continue;
            }

            foreach (var variant in variants)
            {
                if (!countries.Contains(variant, StringComparer.OrdinalIgnoreCase)
                    && !additions.Contains(variant, StringComparer.OrdinalIgnoreCase))
                {
                    additions.Add(variant);
                }
            }
        }

        countries.AddRange(additions);
    }

    private static void ExpandList(List<string> target, IEnumerable<string> extras)
    {
        var seen = new HashSet<string>(target, StringComparer.OrdinalIgnoreCase);
        foreach (var item in extras)
        {
            if (!string.IsNullOrWhiteSpace(item) && seen.Add(item))
            {
                target.Add(item);
            }
        }
    }
}
