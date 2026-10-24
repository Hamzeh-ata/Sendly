using Sendly.Application.Abstractions;

namespace Sendly.Application.Services;

public class SystemDirectoryResolveResult
{
    public List<SystemDirectoryEntry> Entries { get; set; } = [];

    public SourceEligibilityResolveResult Eligibility { get; set; } = new();
}

public class SystemDirectoryEntry
{
    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public List<string> Countries { get; set; } = [];

    public List<string> Industries { get; set; } = [];

    public List<string> PreferredListingUrls { get; set; } = [];

    public string DiscoveryMethod { get; set; } = "DirectoryListing";

    public string SourceKind { get; set; } = "Directory";

    public string Purpose { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public static class SystemDirectoryCatalog
{
    private static readonly string[] ExtraSeedHosts =
    [
        "bizdir24.com",
        "yellowpages.com",
        "ihrsa.org",
        "clubindustry.com",
        "yellowpages.ae",
        "yellowpages.com.eg",
        "yellowpages.com.jo",
        "yellowpages.com.au",
        "yellow.co.nz",
        "dubaibusinessdirectory.com",
        "opencorporates.com",
        "madares.sa",
        "companiesoffice.govt.nz",
        "company-information.service.gov.uk",
        "yell.com",
        "isc.co.uk",
        "fsb.org.uk",
        "ksaschools.com",
        "yaschools.com",
        "saudipedia.com",
        "schrole.com",
        "theinternationalschools.com",
        "expatschoolguide.com",
        "privateschoolreview.com",
        "educationmarket.sa",
        "seriousteachers.com",
        "abadc.com.sa",
        "wikipedia.org",
        "wikiwand.com",
        "dlilsa.com",
        "en.dlilsa.com"
    ];

    private static readonly SystemDirectoryEntry[] Entries =
    [
        new()
        {
            Name = "Madares Saudi Schools",
            BaseUrl = "https://madares.sa",
            Countries = ["Saudi Arabia", "SA", "KSA"],
            Industries = ["Education", "School", "Schools", "Private School"]
        },
        new()
        {
            Name = "Madares Parent Search",
            BaseUrl = "https://parents.madares.sa/school-search",
            Countries = ["Saudi Arabia", "SA", "KSA"],
            Industries = ["Education", "School", "Schools", "Private School"]
        },
        new()
        {
            Name = "Saudi Contractors Authority",
            BaseUrl = "https://sca.sa",
            Countries = ["Saudi Arabia", "SA", "KSA"],
            Industries = ["Construction", "Contractor", "Building", "Engineering"],
            SourceKind = "Association",
            Purpose = "National contractor licensing authority member directory."
        },
        new()
        {
            Name = "Saudi Chambers Directory",
            BaseUrl = "https://www.saudichambers.org.sa",
            Countries = ["Saudi Arabia", "SA", "KSA"],
            Industries =
            [
                "Cleaning", "Commercial Cleaning", "Facility", "Facilities",
                "Hospitality", "Hotel", "Retail", "Business", "Services",
                "Manufacturing", "Logistics", "Transport"
            ],
            SourceKind = "Association",
            Purpose = "Chamber of commerce member listings by region."
        },
        new()
        {
            Name = "Monshaat Saudi SME",
            BaseUrl = "https://www.monshaat.gov.sa",
            Countries = ["Saudi Arabia", "SA", "KSA"],
            Industries =
            [
                "Cleaning", "Commercial Cleaning", "SME", "Business", "Services",
                "Retail", "Hospitality", "Facility Management"
            ]
        },
        new()
        {
            Name = "Saudi Business Center",
            BaseUrl = "https://business.sa",
            Countries = ["Saudi Arabia", "SA", "KSA"],
            Industries = ["Business", "Services", "Cleaning", "Commercial", "Company"]
        },
        new()
        {
            Name = "UAE Yellow Pages",
            BaseUrl = "https://www.yellowpages.ae",
            Countries = ["United Arab Emirates", "UAE", "AE"],
            Industries =
            [
                "Cleaning", "Commercial Cleaning", "Facility", "Construction",
                "Education", "Hospitality", "Hotel", "Business", "Services"
            ]
        },
        new()
        {
            Name = "UAE Business Directory",
            BaseUrl = "https://www.dubaibusinessdirectory.com",
            Countries = ["United Arab Emirates", "UAE", "AE", "Dubai"],
            Industries = ["Cleaning", "Business", "Services", "Construction", "Facility"]
        },
        new()
        {
            Name = "Egypt Yellow Pages",
            BaseUrl = "https://www.yellowpages.com.eg",
            Countries = ["Egypt", "EG"],
            Industries = ["Cleaning", "Business", "Services", "Construction", "Education", "Hospitality"]
        },
        new()
        {
            Name = "Jordan Yellow Pages",
            BaseUrl = "https://www.yellowpages.com.jo",
            Countries = ["Jordan", "JO"],
            Industries = ["Cleaning", "Business", "Services", "Construction", "Education"]
        },
        new()
        {
            Name = "UK Facilities Management Association",
            BaseUrl = "https://www.bia.co.uk",
            Countries = ["United Kingdom", "UK", "GB"],
            Industries = ["Cleaning", "Commercial Cleaning", "Facility", "Facilities Management"],
            SourceKind = "Association",
            Purpose = "Industry association directory for facilities and cleaning members."
        },
        new()
        {
            Name = "ISSA Cleaning Association",
            BaseUrl = "https://www.issa.com",
            Countries = ["United States", "USA", "US", "United Kingdom", "UK", "Saudi Arabia", "UAE"],
            Industries = ["Cleaning", "Commercial Cleaning", "Janitorial", "Facility", "Sanitation"],
            SourceKind = "Association",
            Purpose = "International cleaning association member and education resources."
        },
        new()
        {
            Name = "BSCAI Cleaning Association",
            BaseUrl = "https://www.bscai.org",
            Countries = ["United States", "USA", "US"],
            Industries = ["Cleaning", "Commercial Cleaning", "Building Service"],
            SourceKind = "Association",
            Purpose = "Building service contractors association member directory."
        },
        new()
        {
            Name = "OpenCorporates Search Hub",
            BaseUrl = "https://opencorporates.com",
            Countries =
            [
                "Saudi Arabia", "United Arab Emirates", "United Kingdom", "United States",
                "Egypt", "Jordan", "Canada", "Australia", "New Zealand"
            ],
            Industries =
            [
                "Cleaning", "Commercial Cleaning", "Construction",
                "Hospitality", "Healthcare", "Manufacturing", "Logistics", "Business", "Services",
                "Fitness", "Gym", "Retail", "Property", "Technology"
            ],
            SourceKind = "Registry",
            Purpose = "Multi-jurisdiction company registry search hub for legal entity discovery."
        },
        new()
        {
            Name = "UK Companies House",
            BaseUrl = "https://find-and-update.company-information.service.gov.uk",
            Countries = ["United Kingdom", "UK", "GB"],
            Industries = ["Business", "Services", "Company", "Construction", "Retail", "Education", "Healthcare"],
            SourceKind = "Registry",
            Purpose = "Official UK company registry for registered legal entities."
        },
        new()
        {
            Name = "New Zealand Companies Office",
            BaseUrl = "https://app.companiesoffice.govt.nz",
            Countries = ["New Zealand", "NZ"],
            Industries = ["Business", "Services", "Company", "Retail", "Fitness", "Education", "Healthcare"],
            SourceKind = "Registry",
            Purpose = "Official NZ company register for registered legal entities."
        },
        new()
        {
            Name = "Australia Yellow Pages",
            BaseUrl = "https://www.yellowpages.com.au",
            Countries = ["Australia", "AU"],
            Industries = ["Business", "Services", "Retail", "Construction", "Education", "Healthcare", "Fitness"],
            SourceKind = "Directory",
            Purpose = "National business directory listings by category and location."
        },
        new()
        {
            Name = "New Zealand Yellow Pages",
            BaseUrl = "https://yellow.co.nz",
            Countries = ["New Zealand", "NZ"],
            Industries = ["Business", "Services", "Retail", "Construction", "Education", "Healthcare", "Fitness"],
            SourceKind = "Directory",
            Purpose = "National business directory listings by category and location."
        },
        new()
        {
            Name = "UK Companies House",
            BaseUrl = "https://find-and-update.company-information.service.gov.uk",
            Countries = ["United Kingdom", "UK", "GB", "Britain", "England", "Scotland", "Wales"],
            Industries =
            [
                "Business", "Services", "Education", "School", "Schools", "Cleaning",
                "Construction", "Healthcare", "Retail", "Hospitality", "Manufacturing"
            ],
            SourceKind = "Directory",
            Purpose = "Official UK company registry search for registered organizations."
        },
        new()
        {
            Name = "Independent Schools Council",
            BaseUrl = "https://www.isc.co.uk/schools/",
            Countries = ["United Kingdom", "UK", "GB", "Britain", "England", "Scotland", "Wales"],
            Industries = ["Education", "School", "Schools", "Independent School", "Private School"],
            SourceKind = "Association",
            Purpose = "Association school finder linking to member independent school websites."
        },
        new()
        {
            Name = "NAIS Independent Schools",
            BaseUrl = "https://www.nais.org/school-directory",
            Countries = ["United States", "USA", "US"],
            Industries =
            [
                "Education", "School", "Schools", "Independent School", "Private School",
                "Private Primary School", "Private Secondary School", "Preparatory School"
            ],
            PreferredListingUrls =
            [
                "https://www.nais.org/school-directory"
            ],
            SourceKind = "Association",
            Purpose = "US independent school association member school directory listings."
        },
        new()
        {
            Name = "Private School Review",
            BaseUrl = "https://www.privateschoolreview.com/find-schools",
            Countries = ["United States", "USA", "US"],
            Industries =
            [
                "Education", "School", "Schools", "Independent School", "Private School",
                "Private Primary School", "Private Secondary School", "Preparatory School"
            ],
            PreferredListingUrls =
            [
                "https://www.privateschoolreview.com/find-schools",
                "https://www.privateschoolreview.com/california",
                "https://www.privateschoolreview.com/texas",
                "https://www.privateschoolreview.com/new-york",
                "https://www.privateschoolreview.com/florida"
            ],
            SourceKind = "Directory",
            Purpose = "US private school directory with state and city listings and school profiles."
        },
        new()
        {
            Name = "International Schools Directory",
            BaseUrl = "https://www.theinternationalschools.com",
            Countries =
            [
                "Singapore", "Malaysia", "Vietnam", "Indonesia", "Japan", "Switzerland",
                "United Kingdom", "UK", "United Arab Emirates", "Saudi Arabia", "Egypt", "Jordan"
            ],
            Industries =
            [
                "Education", "School", "Schools", "International School",
                "Private School", "Independent School"
            ],
            SourceKind = "Directory",
            Purpose = "Directory of international and private schools by country outside the US mainland focus."
        },
        new()
        {
            Name = "Expat School Guide",
            BaseUrl = "https://www.expatschoolguide.com",
            Countries =
            [
                "United Kingdom", "UK", "United Arab Emirates", "Saudi Arabia"
            ],
            Industries = ["Education", "School", "Schools", "International School", "Private School"],
            SourceKind = "Directory",
            Purpose = "Guide and directory of international schools for expatriate families."
        },
        new()
        {
            Name = "UK Yell Business Directory",
            BaseUrl = "https://www.yell.com",
            Countries = ["United Kingdom", "UK", "GB", "Britain", "England", "Scotland", "Wales"],
            Industries =
            [
                "Business", "Services", "Cleaning", "Education", "School", "Schools",
                "Construction", "Healthcare", "Retail", "Hospitality", "Fitness"
            ],
            SourceKind = "Directory",
            Purpose = "National UK business directory listings by category and location."
        },
        new()
        {
            Name = "Federation of Small Businesses",
            BaseUrl = "https://www.fsb.org.uk",
            Countries = ["United Kingdom", "UK", "GB", "Britain"],
            Industries = ["Business", "Services", "SME", "Retail", "Construction", "Cleaning"],
            SourceKind = "Association",
            Purpose = "UK small business association member and sector guidance pages."
        },
        new()
        {
            Name = "US Yellow Pages Fitness",
            BaseUrl = "https://www.yellowpages.com",
            Countries = ["United States", "USA", "US"],
            Industries =
            [
                "Gym", "Fitness", "Fitness Club", "Health Club", "Fitness Center", "Fitness Centre",
                "Boutique Fitness Studio", "Personal Training Center", "CrossFit", "Training Facility",
                "Sports Club", "Boxing Gym", "MMA Gym", "Strength & Conditioning", "Wellness Center"
            ],
            PreferredListingUrls =
            [
                "https://www.yellowpages.com/categories/fitness-health-clubs",
                "https://www.yellowpages.com/categories/gymnasiums",
                "https://www.yellowpages.com/categories/personal-trainers",
                "https://www.yellowpages.com/categories/martial-arts-instruction"
            ],
            SourceKind = "Directory",
            Purpose = "US business directory category listings for gyms, fitness clubs, and training facilities."
        },
        new()
        {
            Name = "IHRSA Fitness Association",
            BaseUrl = "https://www.ihrsa.org",
            Countries = ["United States", "USA", "US", "Canada", "United Kingdom", "UK"],
            Industries =
            [
                "Gym", "Fitness", "Fitness Club", "Health Club", "Fitness Center",
                "Boutique Fitness Studio", "Personal Training Center", "Wellness Center"
            ],
            PreferredListingUrls =
            [
                "https://www.ihrsa.org",
                "https://www.ihrsa.org/about/membership/"
            ],
            SourceKind = "Association",
            Purpose = "Global health club and fitness association resources and member-oriented discovery pages."
        },
        new()
        {
            Name = "Club Industry Fitness",
            BaseUrl = "https://www.clubindustry.com",
            Countries = ["United States", "USA", "US"],
            Industries =
            [
                "Gym", "Fitness", "Fitness Club", "Health Club", "Fitness Center",
                "Boutique Fitness Studio", "Personal Training Center", "CrossFit"
            ],
            PreferredListingUrls =
            [
                "https://www.clubindustry.com",
                "https://www.clubindustry.com/facilities"
            ],
            SourceKind = "Directory",
            Purpose = "US fitness facility industry coverage and facility discovery pages."
        },
        new()
        {
            Name = "US Yellow Pages Entertainment Venues",
            BaseUrl = "https://www.yellowpages.com",
            Countries = ["United States", "USA", "US"],
            Industries =
            [
                "Billiards Club", "Snooker Club", "Pool Hall",
                "Paintball Arena", "Laser Tag Arena",
                "Trampoline Park", "Indoor Activity Center"
            ],
            PreferredListingUrls =
            [
                "https://www.yellowpages.com/categories/billiard-parlors",
                "https://www.yellowpages.com/categories/paintball",
                "https://www.yellowpages.com/categories/laser-tag"
            ],
            SourceKind = "Directory",
            Purpose = "US business directory category listings for billiards clubs, paintball and laser tag arenas, and trampoline parks."
        }
    ];

    public static IReadOnlyList<SystemDirectoryEntry> ResolveAssociations(
        IReadOnlyList<string> countries,
        IReadOnlyList<string> industries)
    {
        return Resolve(countries, industries)
            .Where(x => string.Equals(x.SourceKind, "Association", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static IReadOnlyList<SystemDirectoryEntry> AllEntries() => Entries;

    public static bool IsSeedOrDirectoryHost(string? hostOrUrl)
    {
        var host = ExtractHost(hostOrUrl);
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        foreach (var known in ExtraSeedHosts)
        {
            if (HostEqualsOrSubdomain(host, known))
            {
                return true;
            }
        }

        foreach (var entry in Entries)
        {
            var entryHost = ExtractHost(entry.BaseUrl);
            if (!string.IsNullOrWhiteSpace(entryHost) && HostEqualsOrSubdomain(host, entryHost))
            {
                return true;
            }
        }

        return ResearchDirectoryCatalog.IsDirectoryHost(host)
               || SourceExpansionScorer.LooksLikeDirectory($"https://{host}/");
    }

    public static IReadOnlyList<SystemDirectoryEntry> Resolve(
        IReadOnlyList<string> countries,
        IReadOnlyList<string> industries)
    {
        return ResolveWithDecisions(countries, industries).Entries;
    }

    public static SystemDirectoryResolveResult ResolveWithDecisions(
        IReadOnlyList<string> countries,
        IReadOnlyList<string> industries)
    {
        var activeEntries = Entries.Where(x => x.IsActive).ToList();
        var eligibility = SourceEligibilityEvaluator.ResolveMany(
            activeEntries.Select(x => (
                x.Name,
                (string?)x.BaseUrl,
                string.IsNullOrWhiteSpace(x.SourceKind) ? "Directory" : x.SourceKind,
                (IReadOnlyList<string>)x.Countries,
                (IReadOnlyList<string>)x.Industries,
                true,
                false)),
            countries,
            industries);

        var matched = new List<SystemDirectoryEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < activeEntries.Count; i++)
        {
            var decision = eligibility.Decisions[i];
            if (decision.State is not (SourceEligibilityState.Eligible or SourceEligibilityState.EligibleSecondary))
            {
                continue;
            }

            var entry = activeEntries[i];
            if (seen.Add(entry.BaseUrl))
            {
                matched.Add(entry);
            }
        }

        matched = matched
            .OrderByDescending(x => !SecondaryRegistryKinds.Contains(x.SourceKind))
            .ThenBy(x => x.Name)
            .ToList();

        return new SystemDirectoryResolveResult
        {
            Entries = matched,
            Eligibility = eligibility
        };
    }

    private static readonly HashSet<string> SecondaryRegistryKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Registry",
        "Archive",
        "Marketplace"
    };

    private static string? ExtractHost(string? hostOrUrl)
    {
        if (string.IsNullOrWhiteSpace(hostOrUrl))
        {
            return null;
        }

        if (Uri.TryCreate(hostOrUrl, UriKind.Absolute, out var uri))
        {
            return NormalizeHost(uri.Host);
        }

        return NormalizeHost(hostOrUrl);
    }

    private static string NormalizeHost(string host)
    {
        var normalized = host.Trim().ToLowerInvariant();
        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        return normalized;
    }

    private static bool HostEqualsOrSubdomain(string host, string known)
    {
        var normalizedKnown = NormalizeHost(known);
        return host.Equals(normalizedKnown, StringComparison.OrdinalIgnoreCase)
               || host.EndsWith("." + normalizedKnown, StringComparison.OrdinalIgnoreCase);
    }
}
