using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;

namespace Sendly.Application.Services;

public static class ResearchSearchQueryGenerator
{
    private static readonly string[] DefaultBusinessTypes =
    [
        "gym",
        "fitness centre",
        "fitness center",
        "fitness club",
        "health club",
        "CrossFit",
        "bodybuilding gym",
        "strength gym",
        "boutique fitness",
        "training facility",
        "boxing gym",
        "MMA gym",
        "women's fitness"
    ];

    private static readonly string[] DefaultSchoolBusinessTypes =
    [
        "school",
        "private school",
        "international school",
        "British school",
        "American school",
        "Indian school",
        "bilingual school",
        "academy",
        "kindergarten",
        "nursery school",
        "primary school",
        "secondary school",
        "high school",
        "K-12 school"
    ];

    private static readonly string[] DecisionMakerTerms =
    [
        "owner",
        "founder",
        "principal",
        "head of school",
        "director",
        "manager",
        "contact"
    ];

    private static readonly string[] SoftKeywordMarkers =
    [
        "management",
        "administration",
        "software",
        "information system",
        "parent portal",
        "parent communication",
        "student management",
        "school management"
    ];

    private static readonly string[] FrenchBusinessTypes =
    [
        "gymnase",
        "centre de fitness",
        "club de fitness",
        "salle de sport",
        "CrossFit"
    ];

    private static readonly string[] FrenchDecisionMakerTerms =
    [
        "propriétaire",
        "fondateur",
        "gérant",
        "contact"
    ];

    public static readonly string[] ArabicEducationBusinessTypes =
    [
        "مدرسة",
        "مدرسة خاصة",
        "مدرسة دولية",
        "مدرسة أهلية",
        "مدرسة عالمية",
        "روضة أطفال",
        "أكاديمية"
    ];

    private static readonly string[] ArabicDecisionMakerTerms =
    [
        "مدير",
        "مديرة",
        "مؤسس",
        "تواصل",
        "اتصال"
    ];

    private static readonly Dictionary<string, string[]> ArabicCountryCities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Saudi Arabia"] =
        [
            "الرياض", "جدة", "الدمام", "الخبر", "مكة", "المدينة", "الطائف", "تبوك",
            "أبها", "الخبر", "الجبيل", "بريدة", "خميس مشيط", "نجران", "جازان", "ينبع"
        ],
        ["SA"] =
        [
            "الرياض", "جدة", "الدمام", "الخبر", "مكة", "المدينة", "الطائف", "تبوك",
            "أبها", "الجبيل", "بريدة", "خميس مشيط", "نجران", "جازان", "ينبع"
        ],
        ["United Arab Emirates"] =
        [
            "دبي", "أبوظبي", "الشارقة", "عجمان", "رأس الخيمة", "الفجيرة", "العين"
        ],
        ["UAE"] =
        [
            "دبي", "أبوظبي", "الشارقة", "عجمان", "رأس الخيمة", "الفجيرة", "العين"
        ],
        ["Kuwait"] =
        [
            "الكويت", "حولي", "السالمية", "الفروانية", "الجهراء", "الأحمدي"
        ],
        ["KW"] =
        [
            "الكويت", "حولي", "السالمية", "الفروانية", "الجهراء", "الأحمدي"
        ],
        ["Bahrain"] =
        [
            "المنامة", "الرفاع", "المحرق", "مدينة حمد", "مدينة عيسى"
        ],
        ["BH"] =
        [
            "المنامة", "الرفاع", "المحرق", "مدينة حمد", "مدينة عيسى"
        ]
    };

    private static readonly Dictionary<string, string[]> CountryCities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Canada"] =
        [
            "Toronto", "Vancouver", "Calgary", "Edmonton", "Montreal", "Ottawa",
            "Mississauga", "Hamilton", "Winnipeg", "Quebec City", "Victoria", "Halifax"
        ],
        ["CA"] =
        [
            "Toronto", "Vancouver", "Calgary", "Edmonton", "Montreal", "Ottawa",
            "Mississauga", "Hamilton", "Winnipeg", "Quebec City", "Victoria", "Halifax"
        ],
        ["Saudi Arabia"] =
        [
            "Riyadh", "Jeddah", "Dammam", "Khobar", "Al Khobar", "Mecca", "Makkah", "Medina", "Madinah",
            "Tabuk", "Abha", "Taif", "Jubail", "Yanbu", "Buraidah", "Khobar", "Khamis Mushait",
            "Najran", "Jazan", "Hofuf", "Dhahran", "Qatif", "Hail", "Najran"
        ],
        ["SA"] =
        [
            "Riyadh", "Jeddah", "Dammam", "Khobar", "Al Khobar", "Mecca", "Makkah", "Medina", "Madinah",
            "Tabuk", "Abha", "Taif", "Jubail", "Yanbu", "Buraidah", "Khamis Mushait",
            "Najran", "Jazan", "Hofuf", "Dhahran", "Qatif", "Hail"
        ],
        ["KSA"] =
        [
            "Riyadh", "Jeddah", "Dammam", "Khobar", "Al Khobar", "Mecca", "Makkah", "Medina", "Madinah",
            "Tabuk", "Abha", "Taif", "Jubail", "Yanbu", "Buraidah", "Khamis Mushait",
            "Najran", "Jazan", "Hofuf", "Dhahran", "Qatif", "Hail"
        ],
        ["United Arab Emirates"] =
        [
            "Dubai", "Abu Dhabi", "Sharjah", "Ajman", "Ras Al Khaimah", "Fujairah", "Al Ain"
        ],
        ["UAE"] =
        [
            "Dubai", "Abu Dhabi", "Sharjah", "Ajman", "Ras Al Khaimah", "Fujairah", "Al Ain"
        ],
        ["United States"] =
        [
            "New York", "Los Angeles", "Chicago", "Houston", "Miami", "Dallas", "San Francisco",
            "Washington DC", "Boston", "Atlanta", "Seattle", "Philadelphia", "Austin", "Denver", "San Diego",
            "Phoenix", "Orlando", "Las Vegas", "Portland", "Nashville", "Charlotte", "Minneapolis",
            "Tampa", "San Antonio", "Detroit", "Salt Lake City", "Raleigh"
        ],
        ["US"] =
        [
            "New York", "Los Angeles", "Chicago", "Houston", "Miami", "Dallas", "San Francisco",
            "Washington DC", "Boston", "Atlanta", "Seattle", "Philadelphia", "Austin", "Denver", "San Diego",
            "Phoenix", "Orlando", "Las Vegas", "Portland", "Nashville", "Charlotte", "Minneapolis",
            "Tampa", "San Antonio", "Detroit", "Salt Lake City", "Raleigh"
        ],
        ["USA"] =
        [
            "New York", "Los Angeles", "Chicago", "Houston", "Miami", "Dallas", "San Francisco",
            "Washington DC", "Boston", "Atlanta", "Seattle", "Philadelphia", "Austin", "Denver", "San Diego",
            "Phoenix", "Orlando", "Las Vegas", "Portland", "Nashville", "Charlotte", "Minneapolis",
            "Tampa", "San Antonio", "Detroit", "Salt Lake City", "Raleigh"
        ],
        ["United Kingdom"] =
        [
            "London", "Manchester", "Birmingham", "Leeds", "Glasgow", "Edinburgh", "Bristol", "Liverpool"
        ],
        ["UK"] =
        [
            "London", "Manchester", "Birmingham", "Leeds", "Glasgow", "Edinburgh", "Bristol", "Liverpool"
        ],
        ["Finland"] =
        [
            "Helsinki", "Espoo", "Tampere", "Vantaa", "Oulu", "Turku", "Jyväskylä", "Lahti"
        ],
        ["FI"] =
        [
            "Helsinki", "Espoo", "Tampere", "Vantaa", "Oulu", "Turku", "Jyväskylä", "Lahti"
        ],
        ["Kuwait"] =
        [
            "Kuwait City", "Hawalli", "Salmiya", "Farwaniya", "Jahra", "Ahmadi", "Fahaheel", "Mangaf"
        ],
        ["KW"] =
        [
            "Kuwait City", "Hawalli", "Salmiya", "Farwaniya", "Jahra", "Ahmadi", "Fahaheel", "Mangaf"
        ],
        ["Bahrain"] =
        [
            "Manama", "Riffa", "Muharraq", "Hamad Town", "Isa Town", "Sitra", "Budaiya", "Jidhafs"
        ],
        ["BH"] =
        [
            "Manama", "Riffa", "Muharraq", "Hamad Town", "Isa Town", "Sitra", "Budaiya", "Jidhafs"
        ],
        ["Australia"] =
        [
            "Sydney", "Melbourne", "Brisbane", "Perth", "Adelaide", "Gold Coast", "Canberra", "Hobart"
        ],
        ["AU"] =
        [
            "Sydney", "Melbourne", "Brisbane", "Perth", "Adelaide", "Gold Coast", "Canberra", "Hobart"
        ],
        ["Ireland"] =
        [
            "Dublin", "Cork", "Galway", "Limerick", "Waterford", "Kilkenny", "Sligo", "Drogheda"
        ],
        ["IE"] =
        [
            "Dublin", "Cork", "Galway", "Limerick", "Waterford", "Kilkenny", "Sligo", "Drogheda"
        ],
        ["New Zealand"] =
        [
            "Auckland", "Wellington", "Christchurch", "Hamilton", "Tauranga", "Dunedin", "Palmerston North", "Napier"
        ],
        ["NZ"] =
        [
            "Auckland", "Wellington", "Christchurch", "Hamilton", "Tauranga", "Dunedin", "Palmerston North", "Napier"
        ],
        ["Sweden"] =
        [
            "Stockholm", "Gothenburg", "Malmö", "Uppsala", "Västerås", "Örebro", "Linköping", "Helsingborg"
        ],
        ["SE"] =
        [
            "Stockholm", "Gothenburg", "Malmö", "Uppsala", "Västerås", "Örebro", "Linköping", "Helsingborg"
        ],
        ["Norway"] =
        [
            "Oslo", "Bergen", "Trondheim", "Stavanger", "Drammen", "Fredrikstad", "Kristiansand", "Tromsø"
        ],
        ["NO"] =
        [
            "Oslo", "Bergen", "Trondheim", "Stavanger", "Drammen", "Fredrikstad", "Kristiansand", "Tromsø"
        ],
        ["Denmark"] =
        [
            "Copenhagen", "Aarhus", "Odense", "Aalborg", "Esbjerg", "Randers", "Kolding", "Horsens"
        ],
        ["DK"] =
        [
            "Copenhagen", "Aarhus", "Odense", "Aalborg", "Esbjerg", "Randers", "Kolding", "Horsens"
        ],
        ["Netherlands"] =
        [
            "Amsterdam", "Rotterdam", "The Hague", "Utrecht", "Eindhoven", "Groningen", "Tilburg", "Haarlem"
        ],
        ["NL"] =
        [
            "Amsterdam", "Rotterdam", "The Hague", "Utrecht", "Eindhoven", "Groningen", "Tilburg", "Haarlem"
        ]
    };

    private static readonly HashSet<string> QuebecCities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Montreal", "Montréal", "Quebec City", "Québec", "Quebec", "Laval", "Gatineau", "Longueuil"
    };

    private static readonly Regex CityHintRegex = new(
        @"\b(?:in|near|around|city|cities)\s+([A-Z][\p{L}'\-]+(?:\s+[A-Z][\p{L}'\-]+)?)",
        RegexOptions.Compiled);

    private static readonly string[] ExpansionSuffixes =
    [
        "contact",
        "email",
        "website",
        "phone",
        "owner",
        "directory",
        "list",
        "near me",
        "reviews",
        "map"
    ];

    private static readonly string[] ExpansionPrefixes =
    [
        "local",
        "independent",
        "best",
        "top",
        "private"
    ];

    public static IReadOnlyList<string> Generate(ProspectSourceContext context, int maxQueries)
    {
        return GeneratePlan(context, maxQueries).Select(x => x.Query).ToList();
    }

    public static IReadOnlyList<PlannedSearchQuery> GeneratePlan(ProspectSourceContext context, int maxQueries)
    {
        return SearchCampaignPlanner.Build(context, maxQueries);
    }

    public static IReadOnlyList<string> GenerateAdditional(
        ProspectSourceContext context,
        IReadOnlyCollection<string> existingQueries,
        int maxAdditional,
        int expansionRound = 0)
    {
        var limit = Math.Clamp(maxAdditional, 1, 200);
        var queries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in existingQueries)
        {
            if (!string.IsNullOrWhiteSpace(existing))
            {
                seen.Add(Regex.Replace(existing.Trim(), @"\s+", " "));
            }
        }

        if (expansionRound <= 0)
        {
            AppendExpansionQueries(context, queries, seen, limit);
        }
        else if (expansionRound == 1)
        {
            AppendLocalizedQueries(context, queries, seen, limit);
        }
        else if (expansionRound == 2)
        {
            AppendCountryWideAndDirectoryQueries(context, queries, seen, limit);
        }
        else
        {
            AppendDeepVariantQueries(context, queries, seen, limit);
        }

        if (queries.Count == 0 && expansionRound < 3)
        {
            return GenerateAdditional(context, existingQueries, maxAdditional, expansionRound + 1);
        }

        return queries;
    }

    private static void AppendCoreQueries(
        ProspectSourceContext context,
        List<string> queries,
        HashSet<string> seen,
        int limit)
    {
        var countries = ResolveCountries(context);
        var locations = ResolveLocations(context);
        var businessTypes = ResolveBusinessTypes(context);
        var roles = ResolveDecisionMakerTerms(context);
        var excluded = new List<string>();

        var coreTypes = businessTypes.Take(6).ToList();
        if (coreTypes.Count == 0)
        {
            coreTypes.Add("business");
        }

        var directoryBudget = Math.Min(limit, Math.Max(8, limit / 4));
        AppendDirectorySiteQueries(context, queries, seen, directoryBudget, locations, coreTypes);
        if (queries.Count >= limit)
        {
            return;
        }

        foreach (var country in countries)
        {
            var coreLocations = locations.Count > 12 ? locations.Take(12).ToList() : locations;
            var seedType = coreTypes[0];

            foreach (var businessType in coreTypes.Take(4))
            {
                AddQuery(queries, seen, BuildQuery(businessType, string.Empty, country, null, excluded), limit);
                if (queries.Count >= limit)
                {
                    return;
                }
            }

            foreach (var location in coreLocations)
            {
                AddQuery(queries, seen, BuildQuery(seedType, location, country, null, excluded), limit);
                if (queries.Count >= limit)
                {
                    return;
                }
            }

            if (ShouldGenerateArabic(country) && IsApprovedEducationProject(context))
            {
                AppendArabicCoreQueries(queries, seen, limit, country, coreLocations.Take(8).ToList());
                if (queries.Count >= limit)
                {
                    return;
                }
            }

            foreach (var businessType in coreTypes.Skip(1))
            {
                foreach (var location in coreLocations)
                {
                    AddQuery(queries, seen, BuildQuery(businessType, location, country, null, excluded), limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }

            if (LooksLikeExperienceBookingProject(context) || LooksLikePlayWatchVenueProject(context))
            {
                AppendExperienceBookingQueries(
                    queries,
                    seen,
                    limit,
                    coreTypes,
                    coreLocations,
                    excluded);
                if (queries.Count >= limit)
                {
                    return;
                }
            }

            if (LooksLikePlayWatchVenueProject(context))
            {
                AppendPlayWatchVenueQueries(
                    queries,
                    seen,
                    limit,
                    coreTypes,
                    coreLocations,
                    excluded);
                if (queries.Count >= limit)
                {
                    return;
                }
            }

            var primaryType = coreTypes[0];
            foreach (var role in roles.Take(3))
            {
                foreach (var location in coreLocations.Take(8))
                {
                    AddQuery(queries, seen, BuildQuery(primaryType, location, country, role, excluded), limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }

            foreach (var location in coreLocations)
            {
                if (!ShouldGenerateFrench(location, country))
                {
                    continue;
                }

                foreach (var frenchType in FrenchBusinessTypes)
                {
                    AddQuery(queries, seen, BuildFrenchQuery(frenchType, location, null), limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }

                foreach (var frenchRole in FrenchDecisionMakerTerms.Take(2))
                {
                    AddQuery(queries, seen, BuildFrenchQuery(FrenchBusinessTypes[0], location, frenchRole), limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }
        }

        if (queries.Count == 0)
        {
            AddQuery(
                queries,
                seen,
                BuildQuery(
                    businessTypes.Count > 0 ? businessTypes[0] : "business",
                    locations.Count > 0 ? locations[0] : string.Empty,
                    countries.Count > 0 ? countries[0] : string.Empty,
                    "contact",
                    excluded),
                limit);
        }
    }

    private static void AppendExperienceBookingQueries(
        List<string> queries,
        HashSet<string> seen,
        int limit,
        IReadOnlyList<string> businessTypes,
        IReadOnlyList<string> locations,
        IReadOnlyList<string> excluded)
    {
        var intents = new[]
        {
            "booking",
            "reservations",
            "workshops",
            "group events",
            "rental"
        };

        foreach (var businessType in businessTypes.Take(5))
        {
            foreach (var location in locations.Take(10))
            {
                foreach (var intent in intents)
                {
                    var parts = new List<string> { businessType, intent, location };
                    parts.AddRange(excluded);
                    AddQuery(queries, seen, string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x))), limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static void AppendExpansionQueries(
        ProspectSourceContext context,
        List<string> queries,
        HashSet<string> seen,
        int limit)
    {
        var countries = ResolveCountries(context);
        var locations = ResolveLocations(context);
        var businessTypes = ResolveBusinessTypes(context);
        var roles = ResolveDecisionMakerTerms(context);
        var excluded = new List<string>();

        if (businessTypes.Count == 0)
        {
            businessTypes.Add("business");
        }

        foreach (var country in countries)
        {
            foreach (var businessType in businessTypes.Skip(6).Take(8))
            {
                foreach (var location in locations)
                {
                    AddQuery(queries, seen, BuildQuery(businessType, location, country, null, excluded), limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }

            foreach (var businessType in businessTypes.Take(8))
            {
                foreach (var location in locations)
                {
                    foreach (var suffix in ExpansionSuffixes)
                    {
                        AddQuery(
                            queries,
                            seen,
                            BuildExpandedQuery(businessType, location, country, suffix, excluded),
                            limit);
                        if (queries.Count >= limit)
                        {
                            return;
                        }
                    }

                    foreach (var prefix in ExpansionPrefixes)
                    {
                        AddQuery(
                            queries,
                            seen,
                            BuildExpandedQuery($"{prefix} {businessType}", location, country, null, excluded),
                            limit);
                        if (queries.Count >= limit)
                        {
                            return;
                        }
                    }
                }
            }

            foreach (var role in roles.Skip(3).Take(4))
            {
                foreach (var location in locations.Take(12))
                {
                    AddQuery(queries, seen, BuildQuery(businessTypes[0], location, country, role, excluded), limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }

            foreach (var location in locations)
            {
                if (!ShouldGenerateFrench(location, country))
                {
                    continue;
                }

                foreach (var frenchType in FrenchBusinessTypes)
                {
                    foreach (var suffix in ExpansionSuffixes.Take(4))
                    {
                        AddQuery(queries, seen, $"{BuildFrenchQuery(frenchType, location, null)} {suffix}", limit);
                        if (queries.Count >= limit)
                        {
                            return;
                        }
                    }
                }
            }
        }
    }

    private static string BuildExpandedQuery(
        string businessType,
        string location,
        string country,
        string? suffix,
        List<string> excluded)
    {
        var baseQuery = BuildQuery(businessType, location, country, null, excluded);
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return baseQuery;
        }

        return $"{baseQuery} {suffix}".Trim();
    }

    private static void AppendArabicCoreQueries(
        List<string> queries,
        HashSet<string> seen,
        int limit,
        string country,
        List<string> englishLocations)
    {
        var arabicCities = ResolveArabicCities(country, englishLocations);
        foreach (var businessType in ArabicEducationBusinessTypes)
        {
            foreach (var city in arabicCities)
            {
                AddQuery(queries, seen, $"{businessType} {city}", limit);
                if (queries.Count >= limit)
                {
                    return;
                }
            }

            AddQuery(queries, seen, $"{businessType} السعودية", limit);
            if (queries.Count >= limit)
            {
                return;
            }
        }

        foreach (var role in ArabicDecisionMakerTerms.Take(3))
        {
            foreach (var city in arabicCities.Take(8))
            {
                AddQuery(queries, seen, $"{ArabicEducationBusinessTypes[0]} {role} {city}", limit);
                if (queries.Count >= limit)
                {
                    return;
                }
            }
        }
    }

    private static void AppendLocalizedQueries(
        ProspectSourceContext context,
        List<string> queries,
        HashSet<string> seen,
        int limit)
    {
        var countries = ResolveCountries(context);
        var locations = ResolveLocations(context);
        foreach (var country in countries)
        {
            if (!ShouldGenerateArabic(country))
            {
                continue;
            }

            if (IsApprovedEducationProject(context))
            {
                AppendArabicCoreQueries(queries, seen, limit, country, locations);
                if (queries.Count >= limit)
                {
                    return;
                }
            }

            foreach (var businessType in ResolveBusinessTypes(context).Take(6))
            {
                foreach (var city in ResolveArabicCities(country, locations).Take(10))
                {
                    AddQuery(queries, seen, $"{businessType} {city}", limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static void AppendDirectorySiteQueries(
        ProspectSourceContext context,
        List<string> queries,
        HashSet<string> seen,
        int limit,
        IReadOnlyList<string> locations,
        IReadOnlyList<string> businessTypes)
    {
        var sitePrefixes = ResearchDirectoryCatalog.ResolveDirectorySiteQueries(context);
        if (sitePrefixes.Count == 0)
        {
            return;
        }

        var countries = ResolveCountries(context);
        var country = countries.FirstOrDefault() ?? string.Empty;
        var seedType = businessTypes.Count > 0 ? businessTypes[0] : "business";
        var locationSlice = locations.Take(4).ToList();
        if (locationSlice.Count == 0)
        {
            locationSlice.Add(string.Empty);
        }

        foreach (var prefix in sitePrefixes.Take(2))
        {
            AddQuery(queries, seen, $"{prefix} {seedType}".Trim(), limit);
            if (queries.Count >= limit)
            {
                return;
            }

            foreach (var location in locationSlice)
            {
                AddQuery(queries, seen, $"{prefix} {seedType} {location}".Trim(), limit);
                if (queries.Count >= limit)
                {
                    return;
                }
            }

            if (!ShouldGenerateArabic(country))
            {
                continue;
            }

            foreach (var arabicType in ArabicEducationBusinessTypes.Take(2))
            {
                AddQuery(queries, seen, $"{prefix} {arabicType}".Trim(), limit);
                if (queries.Count >= limit)
                {
                    return;
                }

                foreach (var city in ResolveArabicCities(country, locationSlice).Take(3))
                {
                    AddQuery(queries, seen, $"{prefix} {arabicType} {city}".Trim(), limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static void AppendCountryWideAndDirectoryQueries(
        ProspectSourceContext context,
        List<string> queries,
        HashSet<string> seen,
        int limit)
    {
        var countries = ResolveCountries(context);
        var businessTypes = ResolveBusinessTypes(context).Take(8).ToList();
        var locations = ResolveLocations(context);
        AppendDirectorySiteQueries(context, queries, seen, limit, locations, businessTypes);
        if (queries.Count >= limit)
        {
            return;
        }

        var isEducation = IsApprovedEducationProject(context);
        var directorySuffixes = isEducation
            ? new[]
            {
                "directory",
                "list",
                "contact email",
                "website",
                "admission",
                "fees",
                "ranking",
                "near me",
                "madares"
            }
            : new[]
            {
                "directory",
                "list",
                "contact email",
                "website",
                "near me",
                "booking"
            };

        foreach (var country in countries)
        {
            foreach (var businessType in businessTypes)
            {
                AddQuery(queries, seen, $"{PluralizeBusinessType(businessType)} in {country}", limit);
                if (queries.Count >= limit)
                {
                    return;
                }

                AddQuery(queries, seen, $"best {businessType} {country}", limit);
                if (queries.Count >= limit)
                {
                    return;
                }

                foreach (var suffix in directorySuffixes)
                {
                    AddQuery(queries, seen, $"{businessType} {country} {suffix}", limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }

            if (isEducation && ShouldGenerateArabic(country))
            {
                foreach (var arabicType in ArabicEducationBusinessTypes.Take(4))
                {
                    AddQuery(queries, seen, $"دليل {arabicType} السعودية", limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }

                    AddQuery(queries, seen, $"{arabicType} تواصل ايميل", limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static void AppendDeepVariantQueries(
        ProspectSourceContext context,
        List<string> queries,
        HashSet<string> seen,
        int limit)
    {
        var countries = ResolveCountries(context);
        var locations = ResolveLocations(context);
        var businessTypes = ResolveBusinessTypes(context);
        var variants = new[]
        {
            "private",
            "international",
            "British",
            "American",
            "Indian",
            "Canadian",
            "French",
            "IB",
            "accredited",
            "licensed"
        };

        foreach (var country in countries)
        {
            foreach (var location in locations)
            {
                foreach (var variant in variants)
                {
                    var seed = businessTypes.Count > 0 ? businessTypes[0] : "school";
                    AddQuery(queries, seen, $"{variant} {seed} {location} {country}".Trim(), limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }

            foreach (var businessType in businessTypes.Take(5))
            {
                AddQuery(queries, seen, $"{businessType} email contact {country}", limit);
                if (queries.Count >= limit)
                {
                    return;
                }

                AddQuery(queries, seen, $"\"{businessType}\" \"{country}\" contact", limit);
                if (queries.Count >= limit)
                {
                    return;
                }
            }
        }
    }

    public static List<string> ResolveArabicCities(string country, List<string> englishLocations)
    {
        var cities = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ArabicCountryCities.TryGetValue(country, out var mapped))
        {
            foreach (var city in mapped)
            {
                AddUnique(cities, seen, city);
            }
        }

        if (cities.Count == 0)
        {
            foreach (var location in englishLocations)
            {
                AddUnique(cities, seen, location);
            }
        }

        if (cities.Count == 0)
        {
            cities.Add("الرياض");
        }

        return cities;
    }

    public static List<string> ResolveLocations(ProspectSourceContext context)
    {
        var locations = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var countries = ResolveCountries(context);

        foreach (var location in context.Locations)
        {
            if (!LooksLikeCountry(location))
            {
                AddUnique(locations, seen, location);
            }
        }

        foreach (var entry in context.Countries)
        {
            if (!string.IsNullOrWhiteSpace(entry) && !LooksLikeCountry(entry))
            {
                AddUnique(locations, seen, entry.Trim());
            }
        }

        foreach (var keyword in context.Keywords)
        {
            if (LooksLikeLocation(keyword) && !LooksLikeCountry(keyword))
            {
                AddUnique(locations, seen, keyword);
            }
        }

        ExtractLocationsFromText(locations, seen, context.ResearchInstructions);
        ExtractLocationsFromText(locations, seen, context.Description);

        if (locations.Count > 0)
        {
            return locations;
        }

        foreach (var country in countries)
        {
            if (CountryCities.TryGetValue(country, out var cities))
            {
                foreach (var city in cities)
                {
                    AddUnique(locations, seen, city);
                }
            }
        }

        if (locations.Count == 0)
        {
            locations.Add(string.Empty);
        }

        return locations;
    }

    private static List<string> ResolveCountries(ProspectSourceContext context)
    {
        var countries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in context.Countries)
        {
            if (LooksLikeCountry(entry))
            {
                AddUnique(countries, seen, NormalizeCountryName(entry.Trim()));
            }
        }

        if (countries.Count > 0)
        {
            return countries;
        }

        foreach (var entry in context.Countries)
        {
            if (!string.IsNullOrWhiteSpace(entry))
            {
                AddUnique(countries, seen, NormalizeCountryName(entry.Trim()));
            }
        }

        if (countries.Count == 0)
        {
            countries.Add(string.Empty);
        }

        return countries;
    }

    private static string NormalizeCountryName(string value)
    {
        if (string.Equals(value, "SA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "KSA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Kingdom of Saudi Arabia", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "السعودية", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "المملكة العربية السعودية", StringComparison.OrdinalIgnoreCase))
        {
            return "Saudi Arabia";
        }

        if (string.Equals(value, "UAE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "الإمارات", StringComparison.OrdinalIgnoreCase))
        {
            return "United Arab Emirates";
        }

        if (string.Equals(value, "KW", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "الكويت", StringComparison.OrdinalIgnoreCase))
        {
            return "Kuwait";
        }

        if (string.Equals(value, "BH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "البحرين", StringComparison.OrdinalIgnoreCase))
        {
            return "Bahrain";
        }

        return value;
    }

    public static List<string> ResolveApprovedBusinessTypes(ProspectSourceContext context)
        => ResolveBusinessTypes(context);

    public static bool IsApprovedEducationProject(ProspectSourceContext context)
    {
        foreach (var industry in context.Industries)
        {
            if (ContainsEducationMarker(industry))
            {
                return true;
            }
        }

        foreach (var keyword in context.Keywords)
        {
            if (ContainsEducationMarker(keyword))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> ResolveBusinessTypes(ProspectSourceContext context)
    {
        var primary = new List<string>();
        var secondary = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var industry in context.Industries)
        {
            if (string.IsNullOrWhiteSpace(industry) || LooksLikeLocation(industry))
            {
                continue;
            }

            AddUnique(primary, seen, industry.Trim());
        }

        foreach (var keyword in context.Keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword) || LooksLikeLocation(keyword) || LooksLikeCountry(keyword))
            {
                continue;
            }

            var trimmed = keyword.Trim();
            if (IsSoftSearchKeyword(trimmed))
            {
                AddUnique(secondary, seen, trimmed);
            }
            else
            {
                AddUnique(primary, seen, trimmed);
            }
        }

        var types = new List<string>();
        if (IsApprovedEducationProject(context) || IsEducationProject(primary))
        {
            foreach (var defaultType in DefaultSchoolBusinessTypes)
            {
                AddUnique(types, seen, defaultType);
            }

            foreach (var item in primary)
            {
                if (!string.Equals(item, "Education", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(item, "Edu", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(types, seen, item);
                }
            }
        }
        else
        {
            types.AddRange(primary);
        }

        if (types.Count < 8)
        {
            foreach (var item in secondary)
            {
                AddUnique(types, seen, item);
                if (types.Count >= 8)
                {
                    break;
                }
            }
        }

        if (types.Count == 0)
        {
            foreach (var defaultType in DefaultBusinessTypes)
            {
                AddUnique(types, seen, defaultType);
            }
        }
        else if (IsFitnessProject(types)
                 && !IsEducationProject(types)
                 && !LooksLikeExperienceBookingProject(context)
                 && !LooksLikePlayWatchVenueProject(context))
        {
            foreach (var defaultType in DefaultBusinessTypes)
            {
                AddUnique(types, seen, defaultType);
            }
        }

        return types;
    }

    private static List<string> ResolveDecisionMakerTerms(ProspectSourceContext context)
    {
        var roles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in context.TargetRoles)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                continue;
            }

            AddUnique(roles, seen, role.Trim());
            if (roles.Count >= 5)
            {
                break;
            }
        }

        if (roles.Count > 0)
        {
            return roles;
        }

        return DecisionMakerTerms.ToList();
    }

    private static bool IsSoftSearchKeyword(string value)
    {
        foreach (var marker in SoftKeywordMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeCountry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (CountryCities.ContainsKey(trimmed))
        {
            return true;
        }

        return string.Equals(trimmed, "United Arab Emirates", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "UAE", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "USA", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "KSA", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "Kingdom of Saudi Arabia", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "السعودية", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "المملكة العربية السعودية", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "England", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "Scotland", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "Wales", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "Northern Ireland", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFitnessProject(List<string> types)
    {
        foreach (var type in types)
        {
            if (IsClimbingOrExperienceGym(type) || IsPlayWatchVenueType(type))
            {
                continue;
            }

            if (type.Contains("gym", StringComparison.OrdinalIgnoreCase)
                || type.Contains("fitness", StringComparison.OrdinalIgnoreCase)
                || type.Contains("crossfit", StringComparison.OrdinalIgnoreCase)
                || type.Contains("workout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsClimbingOrExperienceGym(string value)
    {
        return value.Contains("climbing", StringComparison.OrdinalIgnoreCase)
               || value.Contains("bouldering", StringComparison.OrdinalIgnoreCase)
               || value.Contains("escape", StringComparison.OrdinalIgnoreCase)
               || value.Contains("archery", StringComparison.OrdinalIgnoreCase)
               || value.Contains("kayak", StringComparison.OrdinalIgnoreCase)
               || value.Contains("go-kart", StringComparison.OrdinalIgnoreCase)
               || value.Contains("go kart", StringComparison.OrdinalIgnoreCase)
               || value.Contains("karting", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendPlayWatchVenueQueries(
        List<string> queries,
        HashSet<string> seen,
        int limit,
        IReadOnlyList<string> businessTypes,
        IReadOnlyList<string> locations,
        IReadOnlyList<string> excluded)
    {
        var intents = new[]
        {
            "reservations",
            "sessions",
            "walk-in",
            "group bookings",
            "birthday parties"
        };

        foreach (var businessType in businessTypes.Take(7))
        {
            foreach (var location in locations.Take(10))
            {
                foreach (var intent in intents)
                {
                    var parts = new List<string> { businessType, intent, location };
                    parts.AddRange(excluded);
                    AddQuery(queries, seen, string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x))), limit);
                    if (queries.Count >= limit)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static bool LooksLikePlayWatchVenueProject(ProspectSourceContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.ProjectName)
            && context.ProjectName.Contains("PlayWatch", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var markers = new[]
        {
            "billiards",
            "snooker",
            "pool hall",
            "paintball",
            "laser tag",
            "trampoline park",
            "indoor activity center",
            "indoor activity centre"
        };

        foreach (var marker in markers)
        {
            foreach (var keyword in context.Keywords)
            {
                if (ContainsIgnoreCase(keyword, marker))
                {
                    return true;
                }
            }

            foreach (var industry in context.Industries)
            {
                if (ContainsIgnoreCase(industry, marker))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPlayWatchVenueType(string value)
    {
        return value.Contains("billiard", StringComparison.OrdinalIgnoreCase)
               || value.Contains("snooker", StringComparison.OrdinalIgnoreCase)
               || value.Contains("pool hall", StringComparison.OrdinalIgnoreCase)
               || value.Contains("paintball", StringComparison.OrdinalIgnoreCase)
               || value.Contains("laser tag", StringComparison.OrdinalIgnoreCase)
               || value.Contains("trampoline", StringComparison.OrdinalIgnoreCase)
               || value.Contains("indoor activity", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeExperienceBookingProject(ProspectSourceContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.ProjectName)
            && context.ProjectName.Contains("ExperienceOps", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var markers = new[]
        {
            "escape room",
            "archery",
            "pottery",
            "kayak",
            "canoe",
            "climbing gym",
            "bouldering",
            "photography studio",
            "dive center",
            "dive centre",
            "sailing",
            "go-kart",
            "go kart",
            "karting",
            "event equipment rental",
            "bicycle rental",
            "bike rental"
        };

        foreach (var marker in markers)
        {
            foreach (var keyword in context.Keywords)
            {
                if (ContainsIgnoreCase(keyword, marker))
                {
                    return true;
                }
            }

            foreach (var industry in context.Industries)
            {
                if (ContainsIgnoreCase(industry, marker))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsIgnoreCase(string? value, string marker)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEducationProject(List<string> types)
    {
        foreach (var type in types)
        {
            if (ContainsEducationMarker(type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeEducationProject(ProspectSourceContext context)
        => IsApprovedEducationProject(context);

    private static readonly string[] NonEducationSchoolPhrases =
    [
        "sailing school",
        "sail school",
        "dive school",
        "diving school",
        "scuba school",
        "ski school",
        "surf school",
        "surfing school",
        "swim school",
        "swimming school",
        "driving school",
        "flight school",
        "cooking school",
        "culinary school",
        "dance school",
        "music school",
        "yoga school",
        "martial arts school"
    ];

    private static bool ContainsEducationMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = StripNonEducationSchoolPhrases(value);

        return ContainsWholeWord(normalized, "school")
               || normalized.Contains("education", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("academy", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("kindergarten", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("nursery", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("university", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("كلية", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("مدرسة", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("تعليم", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("روضة", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripNonEducationSchoolPhrases(string value)
    {
        var normalized = value;
        foreach (var phrase in NonEducationSchoolPhrases)
        {
            normalized = Regex.Replace(
                normalized,
                $@"\b{Regex.Escape(phrase)}s?\b",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return normalized;
    }

    private static bool ContainsWholeWord(string value, string word)
    {
        return Regex.IsMatch(
            value,
            $@"\b{Regex.Escape(word)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ShouldGenerateArabic(string country)
    {
        return country.Contains("Saudi", StringComparison.OrdinalIgnoreCase)
               || country.Contains("KSA", StringComparison.OrdinalIgnoreCase)
               || country.Contains("السعودية", StringComparison.OrdinalIgnoreCase)
               || country.Contains("United Arab Emirates", StringComparison.OrdinalIgnoreCase)
               || country.Contains("UAE", StringComparison.OrdinalIgnoreCase)
               || country.Contains("Kuwait", StringComparison.OrdinalIgnoreCase)
               || country.Contains("Bahrain", StringComparison.OrdinalIgnoreCase)
               || country.Contains("Qatar", StringComparison.OrdinalIgnoreCase)
               || country.Contains("Oman", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldGenerateFrench(string location, string country)
    {
        if (QuebecCities.Contains(location))
        {
            return true;
        }

        return country.Contains("Canada", StringComparison.OrdinalIgnoreCase)
               && location.Contains("quebec", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildQuery(
        string businessType,
        string location,
        string country,
        string? role,
        List<string> excluded)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(role))
        {
            parts.Add($"{businessType} {role}");
        }
        else if (!string.IsNullOrWhiteSpace(location))
        {
            parts.Add($"{PluralizeBusinessType(businessType)} in {location}");
        }
        else
        {
            parts.Add(businessType);
        }

        if (!string.IsNullOrWhiteSpace(location) && !string.IsNullOrWhiteSpace(role))
        {
            parts.Add(location);
        }

        if (!string.IsNullOrWhiteSpace(country)
            && !string.Equals(country, location, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(country);
        }

        parts.AddRange(excluded);
        return string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string PluralizeBusinessType(string businessType)
    {
        var trimmed = businessType.Trim();
        if (trimmed.EndsWith('s')
            || trimmed.EndsWith("school", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("education", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("ion", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("ment", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return $"{trimmed}s";
    }

    private static string FormatExcludedTerm(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return $"-\"{trimmed}\"";
        }

        return $"-{trimmed}";
    }

    private static string BuildFrenchQuery(string businessType, string location, string? role)
    {
        if (!string.IsNullOrWhiteSpace(role))
        {
            return $"{businessType} {role} {location} Canada".Trim();
        }

        return $"{businessType} {location} Canada".Trim();
    }

    private static void AddQuery(List<string> queries, HashSet<string> seen, string query, int limit)
    {
        if (queries.Count >= limit || string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var normalized = Regex.Replace(query.Trim(), @"\s+", " ");
        if (seen.Add(normalized))
        {
            queries.Add(normalized);
        }
    }

    private static void ExtractLocationsFromText(List<string> locations, HashSet<string> seen, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (Match match in CityHintRegex.Matches(text))
        {
            AddUnique(locations, seen, match.Groups[1].Value);
        }

        foreach (var cities in CountryCities.Values)
        {
            foreach (var city in cities)
            {
                if (text.Contains(city, StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(locations, seen, city);
                }
            }
        }
    }

    private static bool LooksLikeLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var cities in CountryCities.Values)
        {
            foreach (var city in cities)
            {
                if (string.Equals(city, value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return QuebecCities.Contains(value.Trim());
    }

    private static void AddUnique(List<string> values, HashSet<string> seen, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (seen.Add(trimmed))
        {
            values.Add(trimmed);
        }
    }
}
