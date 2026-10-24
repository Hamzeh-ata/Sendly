using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;

namespace Sendly.Application.Services;

public static class SearchCampaignPlanner
{
    private static readonly string[] DiscoverIntents =
    [
        SearchQueryIntents.DiscoverBusiness,
        SearchQueryIntents.Directory,
        SearchQueryIntents.RegionalListing,
        SearchQueryIntents.OfficialWebsite,
        SearchQueryIntents.ContactPage,
        SearchQueryIntents.PublicEmail,
        SearchQueryIntents.Association,
        SearchQueryIntents.BranchListing
    ];

    public static IReadOnlyList<PlannedSearchQuery> Build(ProspectSourceContext context, int maxQueries)
    {
        var limit = Math.Clamp(maxQueries, 1, 200);
        var planned = new List<PlannedSearchQuery>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spec = context.Specification;
        var industries = ResolveIndustries(context, spec);
        var countries = ResolveCountries(context, spec);
        var locations = ResolveLocations(context, spec);
        var isEducation = ResearchSearchQueryGenerator.IsApprovedEducationProject(context);

        if (industries.Count == 0)
        {
            industries.Add("business");
        }

        if (countries.Count == 0)
        {
            countries.Add(string.Empty);
        }

        var localizeArabic = spec?.LanguageHints.Contains("ar") == true || ShouldLocalizeArabic(countries);
        var localizeFrench = ShouldLocalizeFrench(context, locations, countries);
        var arabicReserve = localizeArabic ? Math.Min(20, Math.Max(8, limit / 5)) : 0;
        var frenchReserve = localizeFrench ? Math.Min(8, Math.Max(4, limit / 8)) : 0;
        var roleReserve = Math.Min(10, Math.Max(4, limit / 6));
        var coreLimit = Math.Max(8, limit - arabicReserve - frenchReserve - roleReserve);

        AppendDirectoryQueries(context, planned, seen, Math.Min(coreLimit, Math.Max(6, limit / 8)), industries, locations);
        AppendBalancedIndustryLocationQueries(planned, seen, coreLimit, industries, locations, countries);
        if (localizeFrench)
        {
            AppendFrenchQueries(planned, seen, limit, locations);
        }

        if (localizeArabic)
        {
            AppendArabicQueries(planned, seen, limit, industries, countries, locations, isEducation);
        }

        AppendRoleQueries(context, planned, seen, limit, industries, locations, countries);
        AppendIntentQueries(planned, seen, limit, industries, locations, countries);
        var eligibilitySpec = spec ?? ProjectEligibilityPolicy.BuildFallbackSpec(context);
        foreach (var item in planned)
        {
            item.ProjectId = context.ProjectId;
            item.ResearchQueryVersionId = context.QueryVersion?.Id;
            item.Country ??= countries.FirstOrDefault();
            item.Purpose = item.Intent;
            item.ExpectedResultType = item.Intent == SearchQueryIntents.Directory
                ? "directory_or_association"
                : "operating_business";
        }

        return ProjectEligibilityPolicy.FilterValidQueries(planned, eligibilitySpec).Take(limit).ToList();
    }

    public static PlannedSearchQuery? SelectNext(
        IReadOnlyList<PlannedSearchQuery> planned,
        ResearchDiscoveryState state)
    {
        if (planned.Count == 0)
        {
            return null;
        }

        var remaining = planned
            .Where(x => !state.ExecutedSearchQueryKeys.Contains(BuildKey(x)))
            .Where(x => !state.LowYieldQueryKeys.Contains(BuildYieldKey(x)))
            .ToList();
        if (remaining.Count == 0)
        {
            remaining = planned.Where(x => !state.ExecutedSearchQueryKeys.Contains(BuildKey(x))).ToList();
        }

        if (remaining.Count == 0)
        {
            return null;
        }

        var coverage = state.QueriesExecutedByIndustry;
        remaining = remaining
            .OrderBy(x => coverage.TryGetValue(x.Industry, out var count) ? count : 0)
            .ThenBy(x => DiscoverIntents.ToList().IndexOf(x.Intent) is var index && index >= 0 ? index : 99)
            .ThenBy(x => x.Query, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return remaining[0];
    }

    public static string BuildKey(PlannedSearchQuery query)
        => $"{query.Intent}|{query.Industry}|{query.Location}|{query.Query}";

    public static string BuildYieldKey(PlannedSearchQuery query)
        => $"{query.Industry}|{query.Intent}";

    private static void AppendBalancedIndustryLocationQueries(
        List<PlannedSearchQuery> planned,
        HashSet<string> seen,
        int limit,
        List<string> industries,
        List<string> locations,
        List<string> countries)
    {
        var country = countries[0];
        if (locations.Count == 0)
        {
            foreach (var industry in industries)
            {
                Add(
                    planned,
                    seen,
                    limit,
                    industry,
                    null,
                    country,
                    SearchQueryIntents.DiscoverBusiness,
                    BuildDiscoverQuery(industry, null, country));
            }

            return;
        }

        foreach (var industry in industries)
        {
            Add(
                planned,
                seen,
                limit,
                industry,
                null,
                country,
                SearchQueryIntents.RegionalListing,
                BuildDiscoverQuery(industry, null, country));
            if (planned.Count >= limit)
            {
                return;
            }
        }

        foreach (var industry in industries.Take(6))
        {
            Add(
                planned,
                seen,
                limit,
                industry,
                null,
                country,
                SearchQueryIntents.Directory,
                Join(industry, "directory", null, country));
            Add(
                planned,
                seen,
                limit,
                industry,
                null,
                country,
                SearchQueryIntents.ContactPage,
                Join(industry, "contact", null, country));
            if (planned.Count >= limit)
            {
                return;
            }
        }

        var locationIndexByIndustry = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (planned.Count < limit)
        {
            var added = false;
            foreach (var industry in industries)
            {
                var index = locationIndexByIndustry.TryGetValue(industry, out var current) ? current : 0;
                if (index >= locations.Count)
                {
                    continue;
                }

                var location = locations[index];
                locationIndexByIndustry[industry] = index + 1;
                if (Add(
                        planned,
                        seen,
                        limit,
                        industry,
                        location,
                        country,
                        SearchQueryIntents.DiscoverBusiness,
                        BuildDiscoverQuery(industry, location, country)))
                {
                    added = true;
                }

                if (planned.Count >= limit)
                {
                    return;
                }
            }

            if (!added)
            {
                break;
            }
        }
    }

    private static void AppendIntentQueries(
        List<PlannedSearchQuery> planned,
        HashSet<string> seen,
        int limit,
        List<string> industries,
        List<string> locations,
        List<string> countries)
    {
        var country = countries[0];
        var locationSlice = locations.Count == 0 ? new List<string?> { null } : locations.Cast<string?>().Take(8).ToList();
        foreach (var industry in industries)
        {
            foreach (var location in locationSlice)
            {
                Add(
                    planned,
                    seen,
                    limit,
                    industry,
                    location,
                    country,
                    SearchQueryIntents.Directory,
                    Join(industry, "directory", location, country));
                Add(
                    planned,
                    seen,
                    limit,
                    industry,
                    location,
                    country,
                    SearchQueryIntents.ContactPage,
                    Join(industry, "contact", location, country));
                Add(
                    planned,
                    seen,
                    limit,
                    industry,
                    location,
                    country,
                    SearchQueryIntents.PublicEmail,
                    Join(industry, "email", location, country));
                Add(
                    planned,
                    seen,
                    limit,
                    industry,
                    location,
                    country,
                    SearchQueryIntents.OfficialWebsite,
                    Join(industry, "website", location, country));
                Add(
                    planned,
                    seen,
                    limit,
                    industry,
                    location,
                    country,
                    SearchQueryIntents.BranchListing,
                    Join(industry, "location", location, country));
                if (planned.Count >= limit)
                {
                    return;
                }
            }

            Add(
                planned,
                seen,
                limit,
                industry,
                null,
                country,
                SearchQueryIntents.Association,
                Join(industry, "association members", null, country));
            Add(
                planned,
                seen,
                limit,
                industry,
                null,
                country,
                SearchQueryIntents.RegionalListing,
                Join(industry, null, null, country));
        }
    }

    private static void AppendDirectoryQueries(
        ProspectSourceContext context,
        List<PlannedSearchQuery> planned,
        HashSet<string> seen,
        int limit,
        List<string> industries,
        List<string> locations)
    {
        var prefixes = ResearchDirectoryCatalog.ResolveDirectorySiteQueries(context);
        if (prefixes.Count == 0)
        {
            return;
        }

        var country = context.Countries.FirstOrDefault() ?? string.Empty;
        var location = locations.FirstOrDefault();
        foreach (var prefix in prefixes.Take(3))
        {
            foreach (var industry in industries.Take(6))
            {
                Add(
                    planned,
                    seen,
                    limit,
                    industry,
                    location,
                    country,
                    SearchQueryIntents.Directory,
                    $"{prefix} {industry}".Trim());
                if (planned.Count >= limit)
                {
                    return;
                }
            }
        }
    }

    private static void AppendArabicQueries(
        List<PlannedSearchQuery> planned,
        HashSet<string> seen,
        int limit,
        List<string> industries,
        List<string> countries,
        List<string> locations,
        bool isEducation)
    {
        var country = countries.FirstOrDefault() ?? string.Empty;
        var arabicCities = ResearchSearchQueryGenerator.ResolveArabicCities(country, locations);
        if (isEducation)
        {
            foreach (var schoolType in ResearchSearchQueryGenerator.ArabicEducationBusinessTypes)
            {
                foreach (var city in arabicCities.Take(8))
                {
                    Add(planned, seen, limit, schoolType, city, country, SearchQueryIntents.DiscoverBusiness, $"{schoolType} {city}", "ar");
                    if (planned.Count >= limit)
                    {
                        return;
                    }
                }
            }

            return;
        }

        foreach (var industry in industries.Take(8))
        {
            var arabicTerms = ArabicIndustryLexicon.TranslateIndustry(industry);
            var terms = arabicTerms.Count > 0 ? arabicTerms : new[] { industry };
            foreach (var term in terms)
            {
                foreach (var city in arabicCities.Take(6))
                {
                    var arabicCity = ArabicIndustryLexicon.TranslateLocation(city) ?? city;
                    Add(
                        planned,
                        seen,
                        limit,
                        industry,
                        city,
                        country,
                        SearchQueryIntents.DiscoverBusiness,
                        $"{term} {arabicCity}",
                        "ar");
                    if (planned.Count >= limit)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static void AppendFrenchQueries(
        List<PlannedSearchQuery> planned,
        HashSet<string> seen,
        int limit,
        List<string> locations)
    {
        var frenchTypes = new[] { "gymnase", "centre de fitness", "club de fitness", "salle de sport" };
        foreach (var location in locations.Where(x =>
                     x.Contains("Montreal", StringComparison.OrdinalIgnoreCase)
                     || x.Contains("Québec", StringComparison.OrdinalIgnoreCase)
                     || x.Contains("Quebec", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var type in frenchTypes)
            {
                Add(planned, seen, limit, type, location, "Canada", SearchQueryIntents.DiscoverBusiness, $"{type} {location} Canada", "fr");
                if (planned.Count >= limit)
                {
                    return;
                }
            }
        }
    }

    private static void AppendRoleQueries(
        ProspectSourceContext context,
        List<PlannedSearchQuery> planned,
        HashSet<string> seen,
        int limit,
        List<string> industries,
        List<string> locations,
        List<string> countries)
    {
        var roles = context.TargetRoles
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Take(3)
            .ToList();
        if (roles.Count == 0)
        {
            roles.Add("owner");
            roles.Add("contact");
        }

        var country = countries.FirstOrDefault() ?? string.Empty;
        foreach (var industry in industries.Take(4))
        {
            foreach (var location in locations.Take(8))
            {
                foreach (var role in roles.Take(2))
                {
                    Add(
                        planned,
                        seen,
                        limit,
                        industry,
                        location,
                        country,
                        SearchQueryIntents.ContactPage,
                        $"{industry} {role} {location} {country}".Trim());
                    if (planned.Count >= limit)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static List<string> ResolveIndustries(ProspectSourceContext context, ResearchSpecification? spec)
    {
        if (spec is not null && spec.ApprovedIndustries.Count > 0)
        {
            return spec.ApprovedIndustries.ToList();
        }

        return ResearchSearchQueryGenerator.ResolveApprovedBusinessTypes(context);
    }

    private static List<string> ResolveCountries(ProspectSourceContext context, ResearchSpecification? spec)
    {
        if (spec is not null && spec.TargetCountries.Count > 0)
        {
            return spec.TargetCountries.ToList();
        }

        return context.Countries.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
    }

    private static List<string> ResolveLocations(ProspectSourceContext context, ResearchSpecification? spec)
    {
        if (spec is not null && spec.Cities.Count > 0)
        {
            return spec.Cities.ToList();
        }

        return ResearchSearchQueryGenerator.ResolveLocations(context);
    }

    private static bool ShouldLocalizeArabic(List<string> countries)
        => countries.Any(x =>
            x.Contains("United Arab Emirates", StringComparison.OrdinalIgnoreCase)
            || x.Contains("UAE", StringComparison.OrdinalIgnoreCase)
            || x.Contains("Saudi", StringComparison.OrdinalIgnoreCase)
            || x.Contains("Kuwait", StringComparison.OrdinalIgnoreCase)
            || x.Contains("Bahrain", StringComparison.OrdinalIgnoreCase)
            || x.Contains("Qatar", StringComparison.OrdinalIgnoreCase)
            || x.Contains("Oman", StringComparison.OrdinalIgnoreCase));

    private static bool ShouldLocalizeFrench(
        ProspectSourceContext context,
        List<string> locations,
        List<string> countries)
        => countries.Any(x => x.Contains("Canada", StringComparison.OrdinalIgnoreCase))
           && locations.Any(x =>
               x.Contains("Montreal", StringComparison.OrdinalIgnoreCase)
               || x.Contains("Quebec", StringComparison.OrdinalIgnoreCase)
               || x.Contains("Québec", StringComparison.OrdinalIgnoreCase));

    private static string BuildDiscoverQuery(string industry, string? location, string country)
    {
        if (!string.IsNullOrWhiteSpace(location) && IsBookableActivity(industry))
        {
            return Join(industry, "booking", location, country);
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            return Join(industry, "in", location, country);
        }

        return Join(industry, null, null, country);
    }

    private static bool IsBookableActivity(string industry)
    {
        return industry.Contains("escape", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("archery", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("pottery", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("kayak", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("climbing", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("bouldering", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("photography", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("sailing", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("dive", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("go-kart", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("go kart", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("bicycle", StringComparison.OrdinalIgnoreCase)
               || industry.Contains("event equipment", StringComparison.OrdinalIgnoreCase);
    }

    private static string Join(params string?[] parts)
    {
        var cleaned = parts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim());
        return Regex.Replace(string.Join(' ', cleaned), @"\s+", " ").Trim();
    }

    private static bool Add(
        List<PlannedSearchQuery> planned,
        HashSet<string> seen,
        int limit,
        string industry,
        string? location,
        string? country,
        string intent,
        string query,
        string language = "en")
    {
        if (planned.Count >= limit || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var normalized = Regex.Replace(query.Trim(), @"\s+", " ");
        if (!seen.Add($"{intent}|{normalized}"))
        {
            return false;
        }

        planned.Add(new PlannedSearchQuery
        {
            Query = normalized,
            Industry = industry,
            Location = location,
            Country = country,
            Intent = intent,
            Language = language
        });
        return true;
    }
}
