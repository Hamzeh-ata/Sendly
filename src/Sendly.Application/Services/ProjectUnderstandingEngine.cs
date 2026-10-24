using System.Text.Json;
using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;

namespace Sendly.Application.Services;

public static class ProjectUnderstandingEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly string[] ApprovedSectionHeaders =
    [
        "APPROVED INDUSTRIES ONLY",
        "APPROVED INDUSTRIES",
        "TARGET INDUSTRIES",
        "INDUSTRIES"
    ];

    private static readonly string[] ExclusionSectionHeaders =
    [
        "REJECT / EXCLUDE",
        "REJECT/EXCLUDE",
        "EXCLUDE",
        "EXCLUSIONS",
        "DO NOT INCLUDE"
    ];

    private static readonly string[] GoalSectionHeaders =
    [
        "OBJECTIVE",
        "GOAL",
        "MISSION"
    ];

    private static readonly Dictionary<string, string[]> KnownCitiesByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["United Arab Emirates"] =
        [
            "Dubai", "Abu Dhabi", "Sharjah", "Ajman", "Ras Al Khaimah", "Fujairah", "Al Ain"
        ],
        ["UAE"] =
        [
            "Dubai", "Abu Dhabi", "Sharjah", "Ajman", "Ras Al Khaimah", "Fujairah", "Al Ain"
        ],
        ["Saudi Arabia"] =
        [
            "Riyadh", "Jeddah", "Dammam", "Khobar", "Mecca", "Medina", "Tabuk", "Abha", "Taif"
        ],
        ["United States"] =
        [
            "New York", "Los Angeles", "Chicago", "Houston", "Miami", "Dallas", "San Francisco",
            "Washington DC", "Boston", "Atlanta", "Seattle", "Philadelphia", "Austin", "Denver",
            "San Diego", "Phoenix", "Orlando", "Las Vegas", "Portland", "Nashville", "Charlotte",
            "Minneapolis", "Tampa", "San Antonio", "Detroit", "Salt Lake City", "Raleigh"
        ],
        ["USA"] =
        [
            "New York", "Los Angeles", "Chicago", "Houston", "Miami", "Dallas", "San Francisco",
            "Washington DC", "Boston", "Atlanta", "Seattle", "Philadelphia", "Austin", "Denver",
            "San Diego", "Phoenix", "Orlando", "Las Vegas", "Portland", "Nashville", "Charlotte",
            "Minneapolis", "Tampa", "San Antonio", "Detroit", "Salt Lake City", "Raleigh"
        ],
        ["Canada"] =
        [
            "Toronto", "Vancouver", "Calgary", "Edmonton", "Montreal", "Ottawa"
        ]
    };

    private static readonly string[] DefaultExcludedEntityTypes =
    [
        "school", "university", "college", "directory", "booking platform",
        "hotel", "restaurant", "travel agency", "government authority",
        "article", "publisher", "marketplace"
    ];

    public static ResearchSpecification Interpret(ProjectUnderstandingRequest request)
    {
        var spec = new ResearchSpecification
        {
            OriginalBrief = CombineBrief(request),
            MinQualificationScore = request.MinQualificationScore,
            TargetProspectCount = request.TargetProspectCount,
            RequireOfficialWebsite = request.QueryRequireOfficialWebsite,
            RequirePublicBusinessEmail = request.QueryRequirePublicBusinessEmail || request.IncludeEmail,
            Budgets = request.Budgets ?? new ResearchBudgetSpec()
        };

        spec.ProjectGoal = ExtractSection(request.ResearchInstructions, GoalSectionHeaders)
                           ?? TrimTo(request.Description, 800)
                           ?? TrimTo(request.ProjectName, 200);
        spec.ValueProposition = TrimTo(request.Description, 800);

        var briefApproved = ParseListedItems(ExtractSection(request.ResearchInstructions, ApprovedSectionHeaders));
        var briefExcluded = ParseListedItems(ExtractSection(request.ResearchInstructions, ExclusionSectionHeaders));

        spec.ApprovedIndustries = DistinctList(request.ApprovedIndustries);
        if (spec.ApprovedIndustries.Count == 0)
        {
            spec.ApprovedIndustries = DistinctList(briefApproved);
            if (spec.ApprovedIndustries.Count > 0)
            {
                spec.CorrectionReasons.Add("Approved industries were recovered from the project brief because the structured industry list was empty.");
            }
        }

        spec.ExcludedIndustries = DistinctList(request.ExcludedIndustries.Concat(briefExcluded));
        spec.ExcludedKeywords = DistinctList(request.ExcludedKeywords);
        spec.ExcludedEntityTypes = BuildExcludedEntityTypes(spec.ApprovedIndustries, spec.ExcludedIndustries);
        spec.TargetCountries = DistinctList(request.Countries);
        if (spec.TargetCountries.Count == 0 && !string.IsNullOrWhiteSpace(request.QueryTargetCountry))
        {
            spec.TargetCountries.Add(request.QueryTargetCountry.Trim());
        }

        spec.Cities = DistinctList(request.QueryCities);
        if (spec.Cities.Count == 0)
        {
            spec.Cities = InferCities(spec.OriginalBrief, spec.TargetCountries);
        }

        spec.GeographicAliases = BuildGeographicAliases(spec.TargetCountries, spec.Cities);
        spec.IndustrySynonyms = IndustrySynonymCatalog.Expand(spec.ApprovedIndustries)
            .Where(x => !spec.ApprovedIndustries.Contains(x, StringComparer.OrdinalIgnoreCase))
            .ToList();

        RemoveExclusionTermsFromTargets(spec);
        spec.LanguageHints = InferLanguageHints(spec);
        spec.DesiredOrganizationTypes = ["operating business", "independent SMB operator"];
        spec.BusinessModels = InferBusinessModels(spec.OriginalBrief);
        spec.RequiredContactChannels = BuildRequiredChannels(request);
        spec.RequiredCriteria = BuildRequiredCriteria(spec);
        spec.OptionalCriteria = ["decision-maker name", "branch address", "phone"];
        spec.MandatoryGates = BuildMandatoryGates(spec);

        ValidateAgainstQuery(spec, request);
        spec.IsValid = spec.ValidationIssues.All(x =>
            !string.Equals(x.Severity, "error", StringComparison.OrdinalIgnoreCase));
        return spec;
    }

    public static void ApplyToQueryVersion(ResearchQueryVersion version, ResearchSpecification spec)
    {
        version.PlainLanguageBrief ??= spec.OriginalBrief;
        version.TargetCountry = spec.TargetCountries.FirstOrDefault() ?? version.TargetCountry;
        version.TargetIndustry = spec.ApprovedIndustries.FirstOrDefault() ?? version.TargetIndustry;
        version.BusinessCategoriesJson = SerializeList(spec.ApprovedIndustries);
        version.CitiesJson = SerializeList(spec.Cities);
        version.RegionsJson = SerializeList(spec.Regions);
        version.ExclusionCriteriaJson = SerializeList(spec.ExcludedIndustries.Concat(spec.ExcludedEntityTypes));
        version.RequiredCriteriaJson = SerializeList(spec.RequiredCriteria);
        version.RequiredContactInfoJson = SerializeList(spec.RequiredContactChannels);
        version.CompanyTypesJson = SerializeList(spec.DesiredOrganizationTypes);
        version.RequireOfficialWebsite = spec.RequireOfficialWebsite;
        version.RequirePublicBusinessEmail = spec.RequirePublicBusinessEmail;
        version.MaxPagesPerDomain = Math.Clamp(spec.Budgets.MaxPagesPerDomain, 1, 50);
        version.MaxCrawlDepth = Math.Clamp(spec.Budgets.MaxCrawlDepth, 0, 8);
        version.MaxUrlsPerRun = Math.Clamp(spec.Budgets.MaxUrlsPerRun, 1, 2000);
        version.MaxConcurrentRequests = Math.Clamp(spec.Budgets.MaxConcurrentRequests, 1, 10);
        version.StructuredDefinitionJson = JsonSerializer.Serialize(spec, JsonOptions);
    }

    public static ResearchSpecification? TryRead(ResearchQueryVersion? version)
    {
        if (string.IsNullOrWhiteSpace(version?.StructuredDefinitionJson))
        {
            return null;
        }

        try
        {
            var spec = JsonSerializer.Deserialize<ResearchSpecification>(version.StructuredDefinitionJson, JsonOptions);
            if (spec is null || !LooksLikePersistedSpecification(spec))
            {
                return null;
            }

            return spec;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool LooksLikePersistedSpecification(ResearchSpecification spec)
        => spec.ApprovedIndustries.Count > 0
           || spec.TargetCountries.Count > 0
           || !string.IsNullOrWhiteSpace(spec.ProjectGoal)
           || !string.IsNullOrWhiteSpace(spec.OriginalBrief);

    public static List<string> ReadJsonList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions)
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

    private static void ValidateAgainstQuery(ResearchSpecification spec, ProjectUnderstandingRequest request)
    {
        if (spec.ApprovedIndustries.Count == 0)
        {
            spec.ValidationIssues.Add(new ResearchSpecValidationIssue
            {
                Code = "spec.no_approved_industries",
                Message = "No approved target industries were found.",
                Severity = "error"
            });
        }

        if (spec.TargetCountries.Count == 0)
        {
            spec.ValidationIssues.Add(new ResearchSpecValidationIssue
            {
                Code = "spec.no_target_country",
                Message = "No target country was found.",
                Severity = "error"
            });
        }

        var queryIndustries = DistinctList(request.QueryBusinessCategories);
        if (!string.IsNullOrWhiteSpace(request.QueryTargetIndustry))
        {
            queryIndustries.Insert(0, request.QueryTargetIndustry.Trim());
            queryIndustries = DistinctList(queryIndustries);
        }

        if (spec.ApprovedIndustries.Count > 1
            && queryIndustries.Count == 1
            && spec.ApprovedIndustries.Contains(queryIndustries[0], StringComparer.OrdinalIgnoreCase))
        {
            spec.ValidationIssues.Add(new ResearchSpecValidationIssue
            {
                Code = "spec.query_narrowed_industries",
                Message = $"Generated query used only '{queryIndustries[0]}' although the project approves {spec.ApprovedIndustries.Count} industries.",
                Severity = "correction"
            });
            spec.CorrectionReasons.Add("Restored the full approved industry list because the generated query kept only the first industry.");
        }

        foreach (var industry in queryIndustries)
        {
            if (IsExclusionOnlyTerm(industry, spec))
            {
                spec.ValidationIssues.Add(new ResearchSpecValidationIssue
                {
                    Code = "spec.query_uses_exclusion_as_target",
                    Message = $"Query target '{industry}' appears only in exclusion instructions.",
                    Severity = "error"
                });
                spec.IsValid = false;
            }
        }
    }

    private static List<string> BuildExcludedEntityTypes(
        IReadOnlyList<string> approvedIndustries,
        IReadOnlyList<string> excludedIndustries)
    {
        var defaults = DefaultExcludedEntityTypes.AsEnumerable();
        if (IsEducationTargetProject(approvedIndustries))
        {
            defaults = defaults.Where(x => !IsEducationEntityTypeDefault(x));
        }

        return DistinctList(defaults.Concat(excludedIndustries));
    }

    private static bool IsEducationTargetProject(IReadOnlyList<string> approvedIndustries)
    {
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

    private static bool IsEducationEntityTypeDefault(string value)
        => value.Equals("school", StringComparison.OrdinalIgnoreCase)
           || value.Equals("university", StringComparison.OrdinalIgnoreCase)
           || value.Equals("college", StringComparison.OrdinalIgnoreCase);

    private static void RemoveExclusionTermsFromTargets(ResearchSpecification spec)
    {
        var exclusionTerms = new HashSet<string>(
            spec.ExcludedIndustries.Concat(spec.ExcludedKeywords).Concat(spec.ExcludedEntityTypes),
            StringComparer.OrdinalIgnoreCase);

        var cleaned = new List<string>();
        foreach (var industry in spec.ApprovedIndustries)
        {
            if (exclusionTerms.Contains(industry) && !LooksLikeNonEducationSchoolPhrase(industry))
            {
                spec.CorrectionReasons.Add($"Removed '{industry}' from approved industries because it is an exclusion term.");
                continue;
            }

            cleaned.Add(industry);
        }

        spec.ApprovedIndustries = cleaned;
    }

    private static bool IsExclusionOnlyTerm(string value, ResearchSpecification spec)
    {
        if (spec.ApprovedIndustries.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return spec.ExcludedIndustries.Contains(value, StringComparer.OrdinalIgnoreCase)
               || spec.ExcludedKeywords.Contains(value, StringComparer.OrdinalIgnoreCase)
               || spec.ExcludedEntityTypes.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeNonEducationSchoolPhrase(string value)
        => value.Contains("sailing school", StringComparison.OrdinalIgnoreCase)
           || value.Contains("dive school", StringComparison.OrdinalIgnoreCase)
           || value.Contains("scuba school", StringComparison.OrdinalIgnoreCase);

    private static List<string> InferCities(string? brief, List<string> countries)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var haystack = brief ?? string.Empty;
        foreach (var country in countries)
        {
            if (!KnownCitiesByCountry.TryGetValue(country, out var cities))
            {
                continue;
            }

            foreach (var city in cities)
            {
                if (haystack.Contains(city, StringComparison.OrdinalIgnoreCase) && seen.Add(city))
                {
                    result.Add(city);
                }
            }

            if (result.Count == 0)
            {
                foreach (var city in cities)
                {
                    if (seen.Add(city))
                    {
                        result.Add(city);
                    }
                }
            }
        }

        return result;
    }

    private static List<string> BuildGeographicAliases(List<string> countries, List<string> cities)
    {
        var aliases = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var country in countries)
        {
            if (seen.Add(country))
            {
                aliases.Add(country);
            }

            if (country.Contains("United Arab Emirates", StringComparison.OrdinalIgnoreCase) && seen.Add("UAE"))
            {
                aliases.Add("UAE");
            }

            if (country.Contains("Saudi", StringComparison.OrdinalIgnoreCase) && seen.Add("KSA"))
            {
                aliases.Add("KSA");
            }
        }

        foreach (var city in cities)
        {
            if (seen.Add(city))
            {
                aliases.Add(city);
            }
        }

        return aliases;
    }

    private static List<string> InferLanguageHints(ResearchSpecification spec)
    {
        var hints = new List<string> { "en" };
        foreach (var country in spec.TargetCountries)
        {
            if (country.Contains("United Arab Emirates", StringComparison.OrdinalIgnoreCase)
                || country.Contains("Saudi", StringComparison.OrdinalIgnoreCase)
                || country.Contains("Kuwait", StringComparison.OrdinalIgnoreCase)
                || country.Contains("Qatar", StringComparison.OrdinalIgnoreCase)
                || country.Contains("Bahrain", StringComparison.OrdinalIgnoreCase)
                || country.Contains("Oman", StringComparison.OrdinalIgnoreCase)
                || country.Contains("Egypt", StringComparison.OrdinalIgnoreCase))
            {
                hints.Add("ar");
            }

            if (country.Contains("Germany", StringComparison.OrdinalIgnoreCase)
                || country.Contains("Austria", StringComparison.OrdinalIgnoreCase)
                || country.Contains("Switzerland", StringComparison.OrdinalIgnoreCase))
            {
                hints.Add("de");
            }

            if (country.Contains("Canada", StringComparison.OrdinalIgnoreCase)
                || country.Contains("France", StringComparison.OrdinalIgnoreCase)
                || spec.Cities.Any(x => x.Contains("Montreal", StringComparison.OrdinalIgnoreCase)))
            {
                hints.Add("fr");
            }
        }

        return DistinctList(hints);
    }

    private static List<string> InferBusinessModels(string? brief)
    {
        var models = new List<string>();
        var text = brief ?? string.Empty;
        if (text.Contains("rental", StringComparison.OrdinalIgnoreCase))
        {
            models.Add("rental");
        }

        if (text.Contains("workshop", StringComparison.OrdinalIgnoreCase)
            || text.Contains("class", StringComparison.OrdinalIgnoreCase)
            || text.Contains("session", StringComparison.OrdinalIgnoreCase))
        {
            models.Add("bookable session");
        }

        if (text.Contains("booking", StringComparison.OrdinalIgnoreCase)
            || text.Contains("reservation", StringComparison.OrdinalIgnoreCase))
        {
            models.Add("appointment or booking");
        }

        return DistinctList(models);
    }

    private static List<string> BuildRequiredChannels(ProjectUnderstandingRequest request)
    {
        var channels = new List<string>();
        if (request.IncludeEmail || request.QueryRequirePublicBusinessEmail)
        {
            channels.Add("email");
        }

        if (request.IncludeWhatsApp)
        {
            channels.Add("whatsapp");
        }

        channels.Add("official website");
        return DistinctList(channels);
    }

    private static List<string> BuildRequiredCriteria(ResearchSpecification spec)
    {
        var criteria = new List<string> { "organization identity", "approved industry", "target country" };
        if (spec.RequireOfficialWebsite)
        {
            criteria.Add("official website");
        }

        if (spec.RequirePublicBusinessEmail)
        {
            criteria.Add("public business email");
        }

        criteria.Add("not excluded entity");
        return criteria;
    }

    private static List<string> BuildMandatoryGates(ResearchSpecification spec)
    {
        var gates = new List<string>
        {
            "excluded entity type",
            "excluded industry",
            "target country",
            "organization identity"
        };
        if (spec.RequirePublicBusinessEmail)
        {
            gates.Add("required email");
        }

        return gates;
    }

    private static string? ExtractSection(string? brief, IReadOnlyList<string> headers)
    {
        if (string.IsNullOrWhiteSpace(brief))
        {
            return null;
        }

        foreach (var header in headers)
        {
            var match = Regex.Match(
                brief,
                $@"(?:^|\n)\s*{Regex.Escape(header)}\s*\n(?<body>[\s\S]*?)(?=\n[A-Z][A-Z /]{{3,}}\n|$)",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var body = match.Groups["body"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    return body;
                }
            }
        }

        return null;
    }

    private static List<string> ParseListedItems(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in section.Split('\n'))
        {
            var line = Regex.Replace(rawLine.Trim(), @"^\d+[\.\)]\s*", string.Empty);
            line = Regex.Replace(line, @"^[\-\*•]\s*", string.Empty).Trim();
            if (line.Length < 3 || line.Length > 160)
            {
                continue;
            }

            if (seen.Add(line))
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static string CombineBrief(ProjectUnderstandingRequest request)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ProjectName))
        {
            parts.Add(request.ProjectName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            parts.Add(request.Description.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.ResearchInstructions))
        {
            parts.Add(request.ResearchInstructions.Trim());
        }

        return string.Join("\n\n", parts);
    }

    private static List<string> DistinctList(IEnumerable<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private static string SerializeList(IEnumerable<string> values)
    {
        var list = DistinctList(values);
        return list.Count == 0 ? "[]" : JsonSerializer.Serialize(list, JsonOptions);
    }

    private static string? TrimTo(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
