using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public class EntityClassificationResult
{
    public ResearchEntityType EntityType { get; set; } = ResearchEntityType.Unknown;

    public int Confidence { get; set; }

    public string ReasonCode { get; set; } = "entity.unknown";

    public string Explanation { get; set; } = "Entity type could not be determined from available evidence.";
}

public static class EntityClassifier
{
    private static readonly string[] DirectorySignals =
    [
        "directory", "listings", "yellow pages", "yellowpages", "find a", "member directory",
        "business directory", "company directory", "school directory", "school guide",
        "compare schools", "top schools", "best schools", "list of", "registry",
        "دليل", "صفحات صفراء", "سجل الشركات"
    ];

    private static readonly string[] ListicleSignals =
    [
        "best ", "top ", "the best", "near me", "things to do", "reviews of",
        "guide to", "ultimate guide", "our picks", "ranked", "compared",
        "best gyms", "best fitness", "top 10", "top 15", "the 10 best", "the 15 best",
        "schools in ", "companies in ", "businesses in ", "international schools in",
        "options for ", "curriculum international",
        "من أفضل", "أفضل المدارس", "أفضل الشركات", "أفضل 10", "die besten", "top-liste"
    ];

    private static readonly string[] PublisherSignals =
    [
        "news", "article", "blog", "magazine", "editorial", "press release",
        "journalist", "subscribe", "newsletter", "opinion", "review site", "trustpilot",
        "tripadvisor", "yelp", "fresha", "classpass", "pedia", "encyclopedia", "موسوعة",
        "wiki"
    ];

    private static readonly string[] TopicArticleSignals =
    [
        " in the city of ", " in city of ", "education in ", "history of ",
        "overview of ", "introduction to ",
        "في مدينة", "التعليم في", "نبذة عن", "تاريخ "
    ];

    private static readonly string[] MarketplaceSignals =
    [
        "marketplace", "buy now", "add to cart", "shop", "for sale", "booking platform"
    ];

    private static readonly string[] AgencySignals =
    [
        "recruitment", "staffing", "job board", "careers portal", "agency", "consultancy only",
        "employment opportunities", "boarding staff jobs", "browse-jobs", "job vacancies"
    ];

    private static readonly string[] NavigationSignals =
    [
        "find a school", "find a business", "find a company", "search schools",
        "all schools", "types of school", "school search",
        "ابحث عن مدرسة", "دليل المدارس", "الصفحة الرئيسية", "find a ", "browse all"
    ];

    private static readonly string[] GovernmentSignals =
    [
        ".gov", "government", "ministry", "department of", "municipality", "council",
        "public sector", ".edu", "education department",
        "هيئة", "وزارة", "بلدية", "حكومة", "behörde", "ministerium", "amt für"
    ];

    private static readonly string[] LocationSignals =
    [
        " club", " location", " branch", " studio", " campus", " centre", " center",
        "/clubs/", "/locations/", "/studios/", "/branches/"
    ];

    public static EntityClassificationResult Classify(
        string? companyName,
        string? title,
        string? excerpt,
        string? websiteOrUrl,
        DiscoverySourceType? sourceType = null)
    {
        var haystack = Normalize($"{companyName} {title} {excerpt} {websiteOrUrl}");
        if (sourceType is DiscoverySourceType.DirectoryCatalog or DiscoverySourceType.IndustryAssociation)
        {
            return Result(
                ResearchEntityType.DirectoryAggregator,
                90,
                "entity.directory_source",
                "URL originated from a directory or association discovery source.");
        }

        if (LooksLikeEncyclopediaOrPublisherHost(websiteOrUrl)
            || IsTopicArticleName(companyName)
            || IsTopicArticleName(title)
            || LooksLikeTopicArticleContent(haystack, companyName, title))
        {
            return Result(
                ResearchEntityType.PublisherEditorial,
                90,
                "entity.encyclopedia_or_topic",
                "Page or host matches an encyclopedia, wiki, or topic article rather than a single business.");
        }

        if (LooksLikeDirectoryPath(websiteOrUrl))
        {
            return Result(
                ResearchEntityType.DirectoryAggregator,
                88,
                "entity.directory_path",
                "URL path matches a directory or listing section.");
        }

        if (IsListicleName(companyName) || IsListicleName(title) || CountHits(haystack, ListicleSignals) >= 2)
        {
            return Result(
                ResearchEntityType.PublisherEditorial,
                88,
                "entity.listicle",
                "Title or name matches a listicle/editorial ranking page, not a single business.");
        }

        var titleAndUrl = Normalize($"{companyName} {title} {websiteOrUrl}");
        if (CountHits(titleAndUrl, NavigationSignals) >= 1
            || LooksLikeJobOrNavigationPath(websiteOrUrl))
        {
            return Result(
                ResearchEntityType.DirectoryAggregator,
                86,
                "entity.navigation_or_jobs",
                "Page role matches navigation, search, or jobs listing rather than a single business.");
        }

        if (CountHits(haystack, AgencySignals) >= 1 && LooksLikeJobOrNavigationPath(websiteOrUrl))
        {
            return Result(
                ResearchEntityType.AgencyIntermediary,
                84,
                "entity.jobs_page",
                "Page path and signals match a jobs or recruitment listing.");
        }

        if (SystemDirectoryCatalog.IsSeedOrDirectoryHost(websiteOrUrl)
            || CountHits(haystack, DirectorySignals) >= 2)
        {
            return Result(
                ResearchEntityType.DirectoryAggregator,
                85,
                "entity.directory_signals",
                "Page or host signals match a directory/aggregator.");
        }

        if (LooksLikeGovernmentPortal(websiteOrUrl, haystack))
        {
            return Result(
                ResearchEntityType.GovernmentOrInfoPortal,
                75,
                "entity.government_or_info",
                "Page signals match a government or informational portal.");
        }

        if (CountHits(haystack, PublisherSignals) >= 2)
        {
            return Result(
                ResearchEntityType.PublisherEditorial,
                70,
                "entity.publisher",
                "Page signals match a publisher or editorial site.");
        }

        if (CountHits(haystack, MarketplaceSignals) >= 2)
        {
            return Result(
                ResearchEntityType.Marketplace,
                70,
                "entity.marketplace",
                "Page signals match a marketplace.");
        }

        if (CountHits(haystack, AgencySignals) >= 2)
        {
            return Result(
                ResearchEntityType.AgencyIntermediary,
                65,
                "entity.agency",
                "Page signals match an agency or intermediary.");
        }

        if (LooksLikeBusinessLocation(companyName, websiteOrUrl, haystack)
            && !CompanyIdentityResolver.IsGenericName(companyName))
        {
            return Result(
                ResearchEntityType.BusinessLocation,
                70,
                "entity.business_location",
                "Signals match a franchise or business location page.");
        }

        if (!string.IsNullOrWhiteSpace(companyName)
            && !CompanyIdentityResolver.IsGenericName(companyName)
            && !SystemDirectoryCatalog.IsSeedOrDirectoryHost(websiteOrUrl)
            && haystack.Length > 40)
        {
            return Result(
                ResearchEntityType.TargetBusiness,
                55,
                "entity.target_candidate",
                "Page has a non-generic organization name and substantial content.");
        }

        return Result(
            ResearchEntityType.Unknown,
            20,
            "entity.unknown",
            "Insufficient evidence to classify entity type.");
    }

    public static bool IsTopicArticleName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var value = Normalize(name);
        return value.StartsWith("education in ", StringComparison.Ordinal)
               || value.StartsWith("history of ", StringComparison.Ordinal)
               || value.StartsWith("overview of ", StringComparison.Ordinal)
               || value.StartsWith("introduction to ", StringComparison.Ordinal)
               || value.StartsWith("التعليم في", StringComparison.Ordinal)
               || value.Contains(" in the city of ", StringComparison.Ordinal)
               || value.Contains(" in city of ", StringComparison.Ordinal)
               || value.StartsWith("في مدينة", StringComparison.Ordinal);
    }

    private static bool LooksLikeTopicArticleContent(string haystack, string? companyName, string? title)
    {
        if (IsTopicArticleName(companyName) || IsTopicArticleName(title))
        {
            return true;
        }

        return haystack.Contains(" in the city of ", StringComparison.Ordinal)
               || haystack.Contains(" in city of ", StringComparison.Ordinal)
               || haystack.StartsWith("education in ", StringComparison.Ordinal)
               || haystack.Contains(". education in ", StringComparison.Ordinal)
               || haystack.Contains("encyclopedia", StringComparison.Ordinal)
               || haystack.Contains("موسوعة", StringComparison.Ordinal);
    }

    private static bool LooksLikeGovernmentPortal(string? websiteOrUrl, string haystack)
    {
        var host = UrlCanonicalizer.ExtractDomain(websiteOrUrl)?.ToLowerInvariant() ?? string.Empty;
        var isGovHost = host.Contains(".gov.", StringComparison.Ordinal)
                        || host.EndsWith(".gov", StringComparison.Ordinal)
                        || host.Contains("ministry", StringComparison.Ordinal)
                        || host.Contains("municipality", StringComparison.Ordinal);
        if (isGovHost)
        {
            return true;
        }

        var strongHits = CountHits(haystack, [
            "ministry of", "department of education", "government portal",
            "public sector portal", "official government"
        ]);
        return strongHits >= 1 && CountHits(haystack, GovernmentSignals) >= 2;
    }

    private static bool LooksLikeEncyclopediaOrPublisherHost(string? websiteOrUrl)
    {
        var host = UrlCanonicalizer.ExtractDomain(websiteOrUrl)?.ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return host.Contains("pedia", StringComparison.Ordinal)
               || host.Contains("wiki", StringComparison.Ordinal)
               || host.Contains("encyclopedia", StringComparison.Ordinal)
               || host.Contains("dlilsa", StringComparison.Ordinal)
               || host.Contains("yellowpages", StringComparison.Ordinal)
               || host.Contains("schoolguide", StringComparison.Ordinal)
               || host.Contains("schoolsdirectory", StringComparison.Ordinal)
               || host.Contains("businessdirectory", StringComparison.Ordinal);
    }

    private static bool LooksLikeDirectoryPath(string? websiteOrUrl)
    {
        if (string.IsNullOrWhiteSpace(websiteOrUrl)
            || !Uri.TryCreate(websiteOrUrl, UriKind.Absolute, out var uri))
        {
            var raw = (websiteOrUrl ?? string.Empty).ToLowerInvariant();
            return raw.Contains("/directory/", StringComparison.Ordinal)
                   || raw.Contains("/directories/", StringComparison.Ordinal)
                   || raw.Contains("/listings/", StringComparison.Ordinal)
                   || raw.Contains("/member-directory", StringComparison.Ordinal);
        }

        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.Contains("/directory/", StringComparison.Ordinal)
               || path.Contains("/directories/", StringComparison.Ordinal)
               || path.Contains("/listings/", StringComparison.Ordinal)
               || path.Contains("/member-directory", StringComparison.Ordinal);
    }

    public static bool IsAllowedTarget(ResearchEntityType entityType, ResearchEntityType? configuredTarget)
    {
        var target = configuredTarget ?? ResearchEntityType.TargetBusiness;
        if (entityType == ResearchEntityType.Unknown)
        {
            return false;
        }

        if (entityType == target)
        {
            return true;
        }

        return target == ResearchEntityType.TargetBusiness
               && entityType == ResearchEntityType.BusinessLocation;
    }

    public static bool IsListicleName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var value = name.Trim();
        if (value.StartsWith("best ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("top ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("the best", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("the 10", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("the 15", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("list of", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("find a ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("من أفضل", StringComparison.OrdinalIgnoreCase)
            || value.Contains("أفضل المدارس", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" near me", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" schools in ", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" companies in ", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" businesses in ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return CountHits(Normalize(value), ListicleSignals) >= 2;
    }

    private static bool LooksLikeJobOrNavigationPath(string? websiteOrUrl)
    {
        if (string.IsNullOrWhiteSpace(websiteOrUrl))
        {
            return false;
        }

        var purpose = UrlAdmissionPolicy.ClassifyPagePurpose(websiteOrUrl);
        return purpose is UrlPagePurpose.JobListing
            or UrlPagePurpose.Navigation
            or UrlPagePurpose.Listicle;
    }

    private static bool LooksLikeBusinessLocation(string? companyName, string? websiteOrUrl, string haystack)
    {
        var path = websiteOrUrl ?? string.Empty;
        if (path.Contains("/clubs/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/locations/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/studios/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/branches/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return CountHits(haystack, LocationSignals) >= 2
               && !string.IsNullOrWhiteSpace(companyName)
               && companyName.Length > 3;
    }

    private static EntityClassificationResult Result(
        ResearchEntityType type,
        int confidence,
        string reasonCode,
        string explanation)
        => new()
        {
            EntityType = type,
            Confidence = confidence,
            ReasonCode = reasonCode,
            Explanation = explanation
        };

    private static int CountHits(string haystack, IEnumerable<string> signals)
    {
        var count = 0;
        foreach (var signal in signals)
        {
            if (haystack.Contains(Normalize(signal), StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();
}
