using Sendly.Application.Abstractions;

namespace Sendly.Application.Services.MapResearch;

public static class OsmTagCapabilityCatalog
{
    public static IReadOnlyList<OsmTagCapability> All { get; } = BuildCatalog();

    public static IReadOnlyList<OsmTagCapability> FindByConceptTokens(IEnumerable<string> tokens)
    {
        var normalized = NormalizeTokens(tokens);
        if (normalized.Count == 0)
        {
            return [];
        }

        var matches = new List<OsmTagCapability>();
        foreach (var capability in All)
        {
            if (HasTokenOverlap(capability.ConceptTokens, normalized))
            {
                matches.Add(capability);
            }
        }

        return matches;
    }

    public static OsmTagCapability? FindExactTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        foreach (var capability in All)
        {
            if (string.Equals(capability.Tag, tag.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return capability;
            }
        }

        return null;
    }

    private static bool HasTokenOverlap(IReadOnlyList<string> left, HashSet<string> right)
    {
        foreach (var token in left)
        {
            if (right.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> NormalizeTokens(IEnumerable<string> tokens)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            foreach (var part in token.Split([' ', '-', '_', '/', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                var cleaned = part.Trim().ToLowerInvariant();
                if (cleaned.Length >= 3)
                {
                    result.Add(cleaned);
                }
            }
        }

        return result;
    }

    private static List<OsmTagCapability> BuildCatalog()
    {
        return
        [
            Tag("leisure=fitness_centre", "Fitness centre or gym facility", "fitness", "gym", "workout", "health club"),
            Tag("leisure=sports_centre", "Sports centre", "sports", "fitness", "athletic"),
            Tag("leisure=stadium", "Stadium", "stadium", "arena"),
            Tag("leisure=pitch", "Sports pitch", "pitch", "field", "sports"),
            Tag("amenity=gym", "Gym amenity tag used in some regions", "gym", "fitness"),
            Tag("sport=fitness", "Fitness sport tagging", "fitness", "gym"),
            Tag("shop=sports", "Sports retail shop", "sports", "retail", "equipment"),
            Tag("shop=laundry", "Laundry shop", "laundry", "cleaning", "wash"),
            Tag("shop=dry_cleaning", "Dry cleaning shop", "dry", "cleaning", "laundry"),
            Tag("craft=cleaner", "Cleaning craft business", "cleaning", "cleaner", "janitorial"),
            Tag("office=cleaning", "Cleaning company office", "cleaning", "janitorial", "commercial cleaning"),
            Tag("amenity=bureau_de_change", "Currency exchange", "exchange", "currency"),
            Tag("tourism=attraction", "Tourist attraction", "attraction", "activity", "entertainment"),
            Tag("tourism=theme_park", "Theme park", "theme", "park", "attraction"),
            Tag("tourism=museum", "Museum", "museum"),
            Tag("leisure=escape_game", "Escape room / escape game", "escape", "room", "game", "puzzle"),
            Tag("leisure=amusement_arcade", "Amusement arcade", "arcade", "game", "indoor", "activity"),
            Tag("leisure=bowling_alley", "Bowling alley", "bowling", "indoor", "activity"),
            Tag("leisure=hackerspace", "Hackerspace / makerspace", "maker", "hackerspace"),
            Tag("amenity=cinema", "Cinema", "cinema", "movie", "theatre"),
            Tag("amenity=theatre", "Theatre", "theatre", "theater", "performance"),
            Tag("amenity=community_centre", "Community centre", "community", "centre", "center"),
            Tag("amenity=school", "School", "school", "education", "primary", "secondary"),
            Tag("amenity=college", "College", "college", "education"),
            Tag("amenity=university", "University", "university", "education", "campus"),
            Tag("amenity=kindergarten", "Kindergarten", "kindergarten", "preschool", "nursery"),
            Tag("amenity=restaurant", "Restaurant", "restaurant", "dining", "food"),
            Tag("amenity=cafe", "Cafe", "cafe", "coffee"),
            Tag("amenity=fast_food", "Fast food", "fast", "food", "restaurant"),
            Tag("amenity=hospital", "Hospital", "hospital", "medical", "healthcare"),
            Tag("amenity=clinic", "Clinic", "clinic", "medical", "healthcare"),
            Tag("amenity=pharmacy", "Pharmacy", "pharmacy", "drugstore"),
            Tag("amenity=bank", "Bank", "bank", "finance"),
            Tag("amenity=fuel", "Fuel station", "fuel", "petrol", "gas"),
            Tag("amenity=car_wash", "Car wash", "car", "wash", "cleaning"),
            Tag("shop=car", "Car dealer", "car", "auto", "dealer"),
            Tag("shop=beauty", "Beauty salon", "beauty", "salon", "cosmetic"),
            Tag("shop=hairdresser", "Hairdresser", "hair", "salon", "barber"),
            Tag("shop=convenience", "Convenience store", "convenience", "store", "retail"),
            Tag("shop=supermarket", "Supermarket", "supermarket", "grocery", "retail"),
            Tag("office=company", "Company office", "company", "office", "business"),
            Tag("office=advertising_agency", "Advertising agency", "advertising", "agency", "ad agency", "advertising agency", "media agency", "marketing", "marketing agency", "digital agency", "digital marketing", "social media", "branding agency", "creative agency", "public relations", "pr agency"),
            Tag("office=graphic_design", "Graphic design studio", "graphic", "design", "creative", "studio", "branding", "design agency"),
            Tag("office=estate_agent", "Estate agent", "estate", "real", "estate agent", "property"),
            Tag("office=insurance", "Insurance office", "insurance"),
            Tag("office=lawyer", "Lawyer office", "lawyer", "attorney", "legal"),
            Tag("office=it", "IT office", "it", "software", "technology"),
            Tag("craft=electrician", "Electrician", "electrician", "electrical"),
            Tag("craft=plumber", "Plumber", "plumber", "plumbing"),
            Tag("craft=carpenter", "Carpenter", "carpenter", "woodwork"),
            Tag("tourism=hotel", "Hotel", "hotel", "lodging"),
            Tag("tourism=guest_house", "Guest house", "guest", "house", "lodging"),
            Tag("amenity=place_of_worship", "Place of worship", "worship", "mosque", "church", "temple")
        ];
    }

    private static OsmTagCapability Tag(string tag, string description, params string[] tokens)
    {
        return new OsmTagCapability
        {
            Tag = tag,
            Description = description,
            ConceptTokens = tokens.ToList()
        };
    }
}
