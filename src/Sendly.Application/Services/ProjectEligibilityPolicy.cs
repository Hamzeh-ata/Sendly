using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public class ProjectEligibilityDecision
{
    public bool IsEligible { get; set; }

    public ResearchCandidateDisposition Disposition { get; set; } = ResearchCandidateDisposition.NeedsVerification;

    public string ReasonCode { get; set; } = "eligibility.unknown";

    public string Explanation { get; set; } = string.Empty;

    public string? MatchedIndustry { get; set; }

    public string? MatchedExclusion { get; set; }

    public bool MayFetch { get; set; }

    public bool IsDirectory { get; set; }
}

public class ProjectQueryValidationResult
{
    public bool IsValid { get; set; }

    public string ReasonCode { get; set; } = "query.ok";

    public string Explanation { get; set; } = string.Empty;

    public string? MatchedIndustry { get; set; }

    public string? MatchedCountry { get; set; }
}

public static class ProjectEligibilityPolicy
{
    private static readonly string[] VendorTokens =
    [
        "software", "saas", "platform", "crm", "booking system", "booking software",
        "management system", "pos system", "vendor", "solution for", "software for",
        "app for", "tools for", "marketplace for operators"
    ];

    private static readonly string[] OperatorSignals =
    [
        "book now", "our location", "our arena", "our park", "opening hours",
        "visit us", "parties", "tickets", "our courts", "our lanes", "admission"
    ];

    public static ResearchSpecification RequireSpec(ProspectSourceContext context)
        => context.Specification
           ?? BuildFallbackSpec(context);

    public static ResearchSpecification BuildFallbackSpec(ProspectSourceContext context)
        => new()
        {
            ApprovedIndustries = context.Industries.ToList(),
            ExcludedIndustries = context.ExcludedIndustries.ToList(),
            ExcludedKeywords = context.ExcludedKeywords.ToList(),
            TargetCountries = context.Countries.ToList(),
            Cities = context.Locations.ToList(),
            RequiredContactChannels = context.IncludeEmail ? ["email"] : [],
            RequireOfficialWebsite = true,
            RequirePublicBusinessEmail = context.IncludeEmail,
            IsValid = context.Industries.Count > 0 && context.Countries.Count > 0
        };

    public static ProjectQueryValidationResult ValidateQuery(
        string query,
        ResearchSpecification spec,
        string? plannedIndustry = null,
        string? plannedCountry = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return InvalidQuery("query.empty", "Query is empty.");
        }

        var text = query.Trim();
        var industries = ResolveIndustries(spec);
        var countries = ResolveCountries(spec);
        var exclusions = ResolveExclusions(spec);

        if (industries.Count == 0)
        {
            return InvalidQuery("query.no_approved_industry", "No approved industries are configured for this project.");
        }

        var matchedIndustry = MatchIndustry(text, industries) ?? MatchIndustry(plannedIndustry, industries);
        if (string.IsNullOrWhiteSpace(matchedIndustry)
            && !string.IsNullOrWhiteSpace(plannedIndustry)
            && industries.Any(x => x.Equals(plannedIndustry, StringComparison.OrdinalIgnoreCase)))
        {
            matchedIndustry = plannedIndustry;
        }

        if (string.IsNullOrWhiteSpace(matchedIndustry))
        {
            return InvalidQuery(
                "query.industry_mismatch",
                "Query has no meaningful connection to an included project industry.");
        }

        if (LooksLikeExcludedPrimary(text, exclusions, matchedIndustry))
        {
            return InvalidQuery(
                "query.exclusion_conflict",
                "Query primarily targets an excluded category.");
        }

        string? matchedCountry = null;
        if (countries.Count > 0)
        {
            matchedCountry = MatchCountry(text, countries, spec)
                            ?? MatchCountry(plannedCountry, countries, spec);
            if (string.IsNullOrWhiteSpace(matchedCountry)
                && !string.IsNullOrWhiteSpace(plannedCountry)
                && countries.Any(x => CountryScopeHelper.AreSameCountry(x, plannedCountry)))
            {
                matchedCountry = plannedCountry;
            }

            if (string.IsNullOrWhiteSpace(matchedCountry) && !HasAnyCity(text, spec))
            {
                return InvalidQuery(
                    "query.missing_geography",
                    "Query omits required geographic targeting for this project.");
            }
        }

        if (IsTooBroad(text, matchedIndustry))
        {
            return InvalidQuery("query.too_broad", "Query is too broad to produce useful business candidates.");
        }

        return new ProjectQueryValidationResult
        {
            IsValid = true,
            ReasonCode = "query.ok",
            Explanation = "Query aligns with project industries and geography.",
            MatchedIndustry = matchedIndustry,
            MatchedCountry = matchedCountry
        };
    }

    public static ProjectEligibilityDecision EvaluateSearchHit(
        SearchResultGateInput input,
        ResearchSpecification spec)
    {
        var gateInput = CloneGateInput(input, spec);
        var gate = SearchResultGate.Evaluate(gateInput);
        if (gate.Decision is SearchResultDecision.WrongIndustry
            or SearchResultDecision.Irrelevant
            or SearchResultDecision.UnsupportedOrInvalidResult)
        {
            return FromGate(gate, eligible: false, ResearchCandidateDisposition.RejectedBeforeFetch);
        }

        if (gate.Decision == SearchResultDecision.WrongGeography)
        {
            return FromGate(gate, eligible: false, ResearchCandidateDisposition.RejectedWrongCountry);
        }

        var evidence = $"{input.Title} {input.Snippet} {input.Url}";
        if (LooksLikeVendorNotOperator(evidence, ResolveIndustries(spec)))
        {
            return new ProjectEligibilityDecision
            {
                IsEligible = false,
                MayFetch = false,
                Disposition = ResearchCandidateDisposition.RejectedVendorNotOperator,
                ReasonCode = "eligibility.vendor_not_operator",
                Explanation = "Result describes a vendor or software seller for the industry, not an operating target business."
            };
        }

        var exclusion = ExclusionEvidenceClassifier.Classify(
            input.Title,
            input.Title,
            input.Snippet,
            input.Url,
            ResolveExclusions(spec),
            approvedIndustries: ResolveIndustries(spec));
        if (exclusion.IsExcluded)
        {
            return new ProjectEligibilityDecision
            {
                IsEligible = false,
                MayFetch = false,
                Disposition = ResearchCandidateDisposition.RejectedExcludedCategory,
                ReasonCode = exclusion.ReasonCode,
                Explanation = exclusion.Explanation,
                MatchedExclusion = exclusion.MatchedTerm
            };
        }

        if (gate.MayFetch || gate.Decision == SearchResultDecision.Ambiguous || gate.IsDirectory)
        {
            return new ProjectEligibilityDecision
            {
                IsEligible = true,
                MayFetch = gate.MayFetch || gate.IsDirectory || gate.Decision == SearchResultDecision.Ambiguous,
                IsDirectory = gate.IsDirectory,
                Disposition = gate.IsDirectory
                    ? ResearchCandidateDisposition.NeedsVerification
                    : ResearchCandidateDisposition.Discovered,
                ReasonCode = "eligibility.search_admitted",
                Explanation = gate.Reason,
                MatchedIndustry = gate.IndustryEvidence
            };
        }

        return FromGate(gate, eligible: false, ResearchCandidateDisposition.RejectedUnrelated);
    }

    public static ProjectEligibilityDecision EvaluateCandidateEvidence(
        string? companyName,
        string? description,
        string? website,
        string? notes,
        string? countryEvidence,
        ResearchSpecification spec)
    {
        var industries = ResolveIndustries(spec);
        var countries = ResolveCountries(spec);
        var exclusions = ResolveExclusions(spec);
        var haystack = $"{companyName} {description} {notes} {website}";

        var exclusion = ExclusionEvidenceClassifier.Classify(
            companyName,
            companyName,
            description,
            website,
            exclusions,
            approvedIndustries: industries);
        if (exclusion.IsExcluded)
        {
            return new ProjectEligibilityDecision
            {
                IsEligible = false,
                Disposition = ResearchCandidateDisposition.RejectedExcludedCategory,
                ReasonCode = exclusion.ReasonCode,
                Explanation = exclusion.Explanation,
                MatchedExclusion = exclusion.MatchedTerm
            };
        }

        if (LooksLikeVendorNotOperator(haystack, industries))
        {
            return new ProjectEligibilityDecision
            {
                IsEligible = false,
                Disposition = ResearchCandidateDisposition.RejectedVendorNotOperator,
                ReasonCode = "eligibility.vendor_not_operator",
                Explanation = "Evidence describes a vendor selling tools to the industry, not an operator in the industry."
            };
        }

        var industry = IndustryEvidenceClassifier.Classify(
            companyName,
            companyName,
            description,
            notes,
            industries,
            exclusions);
        if (!industry.MatchedApprovedIndustry || industry.IsExcluded)
        {
            return new ProjectEligibilityDecision
            {
                IsEligible = false,
                Disposition = ResearchCandidateDisposition.RejectedWrongIndustry,
                ReasonCode = industry.ReasonCode,
                Explanation = industry.Explanation
            };
        }

        if (countries.Count > 0)
        {
            var countryText = $"{countryEvidence} {description} {notes}";
            if (string.IsNullOrWhiteSpace(countryEvidence)
                || !CountryScopeHelper.MatchesAnyCountry(countryText, countries))
            {
                return new ProjectEligibilityDecision
                {
                    IsEligible = false,
                    Disposition = ResearchCandidateDisposition.NeedsVerification,
                    ReasonCode = "eligibility.country_unverified",
                    Explanation = "Country or operating location is not independently verified from evidence.",
                    MatchedIndustry = industry.AssignedIndustry
                };
            }
        }

        return new ProjectEligibilityDecision
        {
            IsEligible = true,
            Disposition = ResearchCandidateDisposition.IndustryVerified,
            ReasonCode = "eligibility.passed",
            Explanation = "Candidate satisfies project hard constraints with independent evidence.",
            MatchedIndustry = industry.AssignedIndustry
        };
    }

    public static bool AiClaimConflictsWithHardConstraints(
        ResearchSpecification spec,
        string? claimedIndustry,
        string? claimedCountry,
        bool claimedRelevant)
    {
        if (!claimedRelevant)
        {
            return false;
        }

        var industries = ResolveIndustries(spec);
        var countries = ResolveCountries(spec);
        if (industries.Count > 0
            && !string.IsNullOrWhiteSpace(claimedIndustry)
            && MatchIndustry(claimedIndustry, industries) is null
            && !IndustrySynonymCatalog.MatchesAny(claimedIndustry, industries))
        {
            return true;
        }

        if (countries.Count > 0
            && !string.IsNullOrWhiteSpace(claimedCountry)
            && !CountryScopeHelper.MatchesAnyCountry(claimedCountry, countries))
        {
            return true;
        }

        return false;
    }

    public static List<PlannedSearchQuery> FilterValidQueries(
        IEnumerable<PlannedSearchQuery> queries,
        ResearchSpecification spec)
    {
        var result = new List<PlannedSearchQuery>();
        foreach (var query in queries)
        {
            var validation = ValidateQuery(query.Query, spec, query.Industry, query.Country);
            if (!validation.IsValid)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(query.Industry) && !string.IsNullOrWhiteSpace(validation.MatchedIndustry))
            {
                query.Industry = validation.MatchedIndustry;
            }

            if (string.IsNullOrWhiteSpace(query.Country) && !string.IsNullOrWhiteSpace(validation.MatchedCountry))
            {
                query.Country = validation.MatchedCountry;
            }

            result.Add(query);
        }

        return result;
    }

    public static bool LooksLikeVendorNotOperator(string evidence, IReadOnlyList<string> industries)
    {
        var lower = evidence.ToLowerInvariant();
        var hasVendor = VendorTokens.Any(token => lower.Contains(token, StringComparison.Ordinal));
        if (!hasVendor)
        {
            return false;
        }

        if (OperatorSignals.Any(token => lower.Contains(token, StringComparison.Ordinal)))
        {
            return false;
        }

        foreach (var industry in industries)
        {
            if (string.IsNullOrWhiteSpace(industry))
            {
                continue;
            }

            var phrase = industry.Trim().ToLowerInvariant();
            if (lower.Contains("for " + phrase, StringComparison.Ordinal)
                || lower.Contains(phrase + " software", StringComparison.Ordinal)
                || lower.Contains(phrase + " operators", StringComparison.Ordinal)
                || lower.Contains("software for " + phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return hasVendor && industries.Any(industry =>
            !string.IsNullOrWhiteSpace(industry)
            && lower.Contains(industry.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static ProjectEligibilityDecision FromGate(
        SearchResultEvaluation gate,
        bool eligible,
        ResearchCandidateDisposition disposition)
        => new()
        {
            IsEligible = eligible,
            MayFetch = eligible && gate.MayFetch,
            IsDirectory = gate.IsDirectory,
            Disposition = disposition,
            ReasonCode = "gate." + gate.Decision,
            Explanation = gate.Reason,
            MatchedIndustry = gate.IndustryEvidence
        };

    private static SearchResultGateInput CloneGateInput(SearchResultGateInput input, ResearchSpecification spec)
        => new()
        {
            Title = input.Title,
            Snippet = input.Snippet,
            Url = input.Url,
            Query = input.Query,
            QueryIndustry = input.QueryIndustry,
            Industries = ResolveIndustries(spec),
            ExcludedIndustries = ResolveExclusions(spec),
            ExcludedKeywords = spec.ExcludedKeywords.ToList(),
            Countries = ResolveCountries(spec),
            Locations = spec.Cities.Count > 0 ? spec.Cities.ToList() : input.Locations.ToList()
        };

    private static List<string> ResolveIndustries(ResearchSpecification spec)
        => Distinct(spec.ApprovedIndustries);

    private static List<string> ResolveCountries(ResearchSpecification spec)
        => Distinct(spec.TargetCountries);

    private static List<string> ResolveExclusions(ResearchSpecification spec)
        => Distinct(spec.ExcludedIndustries.Concat(spec.ExcludedKeywords).Concat(spec.ExcludedEntityTypes));

    private static List<string> Distinct(IEnumerable<string> values)
        => values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? MatchIndustry(string? text, IReadOnlyList<string> industries)
    {
        if (string.IsNullOrWhiteSpace(text) || industries.Count == 0)
        {
            return null;
        }

        return IndustrySynonymCatalog.MatchIndustry(text, industries)
               ?? industries.FirstOrDefault(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private static string? MatchCountry(string? text, IReadOnlyList<string> countries, ResearchSpecification spec)
    {
        if (string.IsNullOrWhiteSpace(text) || countries.Count == 0)
        {
            return null;
        }

        foreach (var country in countries)
        {
            if (CountryScopeHelper.AreSameCountry(text, country)
                || text.Contains(country, StringComparison.OrdinalIgnoreCase))
            {
                return country;
            }

            foreach (var alias in spec.GeographicAliases)
            {
                if (text.Contains(alias, StringComparison.OrdinalIgnoreCase)
                    && CountryScopeHelper.AreSameCountry(alias, country))
                {
                    return country;
                }
            }
        }

        return null;
    }

    private static bool HasAnyCity(string text, ResearchSpecification spec)
    {
        foreach (var city in spec.Cities)
        {
            if (!string.IsNullOrWhiteSpace(city) && text.Contains(city, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeExcludedPrimary(string text, IReadOnlyList<string> exclusions, string matchedIndustry)
    {
        foreach (var exclusion in exclusions)
        {
            if (string.IsNullOrWhiteSpace(exclusion) || exclusion.Length < 4)
            {
                continue;
            }

            if (!text.Contains(exclusion, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (matchedIndustry.Contains(exclusion, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IndustrySynonymCatalog.MatchesAny(exclusion, [matchedIndustry]))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsTooBroad(string text, string matchedIndustry)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length <= 2
            && !text.Contains(matchedIndustry, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return text.Equals("business", StringComparison.OrdinalIgnoreCase)
               || text.Equals("companies", StringComparison.OrdinalIgnoreCase)
               || text.Equals("near me", StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectQueryValidationResult InvalidQuery(string code, string explanation)
        => new()
        {
            IsValid = false,
            ReasonCode = code,
            Explanation = explanation
        };
}
