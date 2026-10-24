using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public class UrlPriorityScore
{
    public int Total { get; set; }

    public int Relevance { get; set; }

    public int PagePurpose { get; set; }

    public int SourceQuality { get; set; }

    public int InformationGain { get; set; }

    public int CostPenalty { get; set; }

    public string ReasonCode { get; set; } = "priority.scored";

    public string Explanation { get; set; } = string.Empty;

    public bool IsDiscoveryUseful => Total >= 35;

    public bool IsVerificationUseful => PagePurpose >= 20 || Total >= 40;
}

public static class UrlPriorityScorer
{
    private static readonly string[] ContactHints =
    [
        "contact", "about", "team", "people", "admissions", "enquire", "enquir", "تواصل", "اتصل"
    ];

    private static readonly string[] NoiseHints =
    [
        "privacy", "terms", "cookie", "login", "signin", "cart", "cdn.", "hubspotusercontent",
        "facebook.com", "linkedin.com", "twitter.com", "instagram.com", "youtube.com",
        "sso.", "/sessions/new", "request-password", "request-signup", "/bylaws",
        "public-policy", "history-of-", "/careers", "/jobs", "legal-information",
        "user-privacy", "terms-of-use", "bulk-pricing", "api-developer"
    ];

    private static readonly string[] TargetListingHints =
    [
        "/school-directory", "/school/", "/schools/", "find-schools", "/find-a-school",
        "-profile", "/members/", "/member/", "/listing/", "/listings/", "/directory/"
    ];

    private static readonly string[] OfficialInvestigationHints =
    [
        "/contact", "/admissions", "/admission", "/about", "/location", "/locations"
    ];

    private static readonly string[] HighVolumePortalHints =
    [
        "sites.moe.gov.", "moe.gov.", "my.gov.", "gov.sa/wps", "portal.gov.",
        "hrsd.gov.", "absher.sa", "najiz.sa", "tawakkalna", "gov.uk/government"
    ];

    public static UrlPriorityScore Score(
        string url,
        DiscoverySourceType sourceType,
        IReadOnlyList<string> industries,
        IReadOnlyList<string> countries,
        string? parentDomain,
        int depth,
        bool alreadyCrawled,
        UrlPagePurpose? pagePurpose = null,
        int domainFetchCount = 0,
        bool domainPreviouslyFailed = false,
        bool isFreshSource = true)
    {
        var score = new UrlPriorityScore();
        var host = UrlCanonicalizer.ExtractDomain(url) ?? string.Empty;
        var haystack = url.ToLowerInvariant();
        var purpose = pagePurpose ?? UrlAdmissionPolicy.ClassifyPagePurpose(url);

        score.SourceQuality = sourceType switch
        {
            DiscoverySourceType.IndustryAssociation => 40,
            DiscoverySourceType.DirectoryCatalog => 30,
            DiscoverySourceType.SeedUrl => 35,
            DiscoverySourceType.PriorResearch => 12,
            DiscoverySourceType.Sitemap => 18,
            DiscoverySourceType.LinkExpansion => 8,
            DiscoverySourceType.WebArchive => 10,
            _ => 5
        };

        if (isFreshSource && sourceType is not DiscoverySourceType.PriorResearch)
        {
            score.SourceQuality += 6;
        }

        score.Relevance = CountListHits(haystack, industries) * 8
                          + CountListHits(haystack, countries) * 6;
        if (CountryScopeHelper.HostMatchesCountries(host, countries)
            && CountryScopeHelper.InferCountryFromHost(host) is not null)
        {
            score.Relevance += 12;
        }
        else if (!CountryScopeHelper.HostMatchesCountries(host, countries))
        {
            score.CostPenalty += 40;
        }

        score.PagePurpose = ScorePagePurpose(haystack, purpose);
        if (!string.IsNullOrWhiteSpace(parentDomain)
            && host.Equals(parentDomain, StringComparison.OrdinalIgnoreCase))
        {
            score.InformationGain += purpose is UrlPagePurpose.Contact
                or UrlPagePurpose.About
                or UrlPagePurpose.Team
                ? 22
                : 15;
        }
        else if (!string.IsNullOrWhiteSpace(parentDomain))
        {
            score.InformationGain += 4;
            score.CostPenalty += 8;
        }

        if (purpose == UrlPagePurpose.FranchiseLocation)
        {
            score.InformationGain += 10;
        }

        if (purpose is UrlPagePurpose.Contact or UrlPagePurpose.About or UrlPagePurpose.Team
            && sourceType is DiscoverySourceType.Sitemap or DiscoverySourceType.LinkExpansion
            && !string.IsNullOrWhiteSpace(parentDomain))
        {
            score.InformationGain += 12;
            score.ReasonCode = "priority.contact_on_business_domain";
        }

        var looksLikeTargetListing = ContainsAny(haystack, TargetListingHints);
        var looksLikeOfficialInvestigation = ContainsAny(haystack, OfficialInvestigationHints)
                                             && !SystemDirectoryCatalog.IsSeedOrDirectoryHost(host);
        if (looksLikeTargetListing)
        {
            score.InformationGain += 28;
            score.ReasonCode = "priority.target_listing";
        }
        else if (looksLikeOfficialInvestigation)
        {
            score.InformationGain += 24;
            score.ReasonCode = "priority.official_investigation";
        }
        else if (purpose == UrlPagePurpose.DirectoryListing)
        {
            score.CostPenalty += 18;
            score.InformationGain += 4;
            score.ReasonCode = "priority.directory_chrome";
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var scoredUri)
            && CountryScopeHelper.InferCountryFromPath(scoredUri.AbsolutePath) is { } pathCountry)
        {
            if (CountryScopeHelper.CountryMatches(pathCountry, countries))
            {
                score.Relevance += 20;
                score.InformationGain += 12;
            }
            else if (countries.Count > 0)
            {
                score.CostPenalty += 50;
                score.ReasonCode = "priority.foreign_path";
            }
        }

        if (host.StartsWith("sso.", StringComparison.OrdinalIgnoreCase)
            || host.Contains("opencorporates", StringComparison.OrdinalIgnoreCase)
            || host.Contains("closeup.org", StringComparison.OrdinalIgnoreCase)
            || host.Contains("votervoice.net", StringComparison.OrdinalIgnoreCase))
        {
            score.CostPenalty += 60;
            score.ReasonCode = "priority.low_value_host";
        }

        if (sourceType == DiscoverySourceType.PriorResearch
            && purpose is UrlPagePurpose.CompanyProfile or UrlPagePurpose.Unknown)
        {
            score.CostPenalty += 10;
        }

        var isHighValuePurpose = purpose is UrlPagePurpose.Contact
            or UrlPagePurpose.About
            or UrlPagePurpose.Team
            or UrlPagePurpose.Location
            or UrlPagePurpose.CompanyProfile
            or UrlPagePurpose.DirectoryListing;
        if (depth > 1)
        {
            score.CostPenalty += isHighValuePurpose
                ? Math.Max(0, (depth - 1) * 2)
                : depth * 6;
        }

        if (alreadyCrawled)
        {
            score.CostPenalty += 50;
        }

        if (domainFetchCount > 0 && !isHighValuePurpose)
        {
            score.CostPenalty += Math.Min(30, domainFetchCount * 8);
        }
        else if (domainFetchCount > 0)
        {
            score.CostPenalty += Math.Min(12, domainFetchCount * 3);
        }

        if (domainPreviouslyFailed)
        {
            score.CostPenalty += 25;
        }

        if (NoiseHints.Any(x => haystack.Contains(x, StringComparison.Ordinal)))
        {
            score.CostPenalty += 35;
        }

        if (HighVolumePortalHints.Any(x => haystack.Contains(x, StringComparison.Ordinal))
            || host.Contains(".gov.", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".gov", StringComparison.OrdinalIgnoreCase))
        {
            score.CostPenalty += 28;
            if (purpose is not UrlPagePurpose.DirectoryListing
                and not UrlPagePurpose.CompanyProfile
                and not UrlPagePurpose.Contact
                and not UrlPagePurpose.About)
            {
                score.CostPenalty += 18;
            }
        }

        var offIndustry = LooksLikeOffIndustryNoise(haystack, host, industries);
        if (offIndustry)
        {
            score.CostPenalty += 45;
            score.ReasonCode = "priority.off_industry_noise";
        }

        if (score.Relevance == 0
            && industries.Count > 0
            && sourceType is DiscoverySourceType.PriorResearch or DiscoverySourceType.LinkExpansion
            && purpose is not UrlPagePurpose.Contact and not UrlPagePurpose.About)
        {
            score.CostPenalty += 20;
        }

        if (purpose is UrlPagePurpose.CompanyProfile or UrlPagePurpose.Contact or UrlPagePurpose.About
            && sourceType is DiscoverySourceType.DirectoryCatalog or DiscoverySourceType.LinkExpansion
            && (haystack.Contains("/school", StringComparison.Ordinal)
                || haystack.Contains("/profile", StringComparison.Ordinal)
                || haystack.Contains("/listing", StringComparison.Ordinal)
                || haystack.Contains("/company", StringComparison.Ordinal)
                || haystack.Contains("contact", StringComparison.Ordinal)))
        {
            score.InformationGain += 10;
        }

        score.Total = Math.Max(
            0,
            score.SourceQuality + score.Relevance + score.PagePurpose + score.InformationGain - score.CostPenalty);
        score.Explanation =
            $"priority={score.Total} source={score.SourceQuality} relevance={score.Relevance} purpose={score.PagePurpose} gain={score.InformationGain} cost=-{score.CostPenalty} page={purpose}";
        if (!offIndustry)
        {
            score.ReasonCode = $"priority.{purpose.ToString().ToLowerInvariant()}";
        }

        return score;
    }

    private static int ScorePagePurpose(string haystack, UrlPagePurpose purpose)
    {
        var score = purpose switch
        {
            UrlPagePurpose.Contact => 28,
            UrlPagePurpose.Team => 22,
            UrlPagePurpose.About => 18,
            UrlPagePurpose.FranchiseLocation => 20,
            UrlPagePurpose.Location => 16,
            UrlPagePurpose.CompanyProfile => 12,
            UrlPagePurpose.DirectoryListing => 16,
            _ => 0
        };

        foreach (var hint in ContactHints)
        {
            if (haystack.Contains(hint, StringComparison.Ordinal))
            {
                score += hint.StartsWith("contact", StringComparison.Ordinal) ? 8 : 4;
            }
        }

        return Math.Min(score, 40);
    }

    private static int CountListHits(string haystack, IReadOnlyList<string> values)
    {
        var count = 0;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 3)
            {
                continue;
            }

            if (haystack.Contains(value.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                count++;
            }
        }

        return Math.Min(count, 4);
    }

    private static bool ContainsAny(string haystack, IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeOffIndustryNoise(
        string haystack,
        string host,
        IReadOnlyList<string> industries)
    {
        if (industries.Count == 0)
        {
            return false;
        }

        foreach (var entry in SystemDirectoryCatalog.AllEntries())
        {
            var entryHost = UrlCanonicalizer.ExtractDomain(entry.BaseUrl);
            if (string.IsNullOrWhiteSpace(entryHost)
                || !host.Equals(entryHost, StringComparison.OrdinalIgnoreCase)
                    && !host.EndsWith("." + entryHost, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IndustrySynonymCatalog.FindIndustryOverlap(industries, entry.Industries) is null)
            {
                return true;
            }
        }

        var schoolFocused = industries.Any(x =>
            !string.IsNullOrWhiteSpace(x)
            && (x.Contains("school", StringComparison.OrdinalIgnoreCase)
                || x.Contains("education", StringComparison.OrdinalIgnoreCase)
                || x.Contains("academy", StringComparison.OrdinalIgnoreCase)));
        if (!schoolFocused)
        {
            return false;
        }

        return haystack.Contains("cannabis", StringComparison.Ordinal)
               || haystack.Contains("weed", StringComparison.Ordinal)
               || host.Contains("cannabis", StringComparison.OrdinalIgnoreCase)
               || host.Contains("cleaning", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains("/careers", StringComparison.Ordinal)
               || haystack.Contains("referrals.selectminds", StringComparison.Ordinal)
               || haystack.Contains("fticonsulting", StringComparison.Ordinal)
               || host.Contains("opencorporates", StringComparison.OrdinalIgnoreCase)
               || (host.EndsWith(".edu", StringComparison.OrdinalIgnoreCase)
                   && !haystack.Contains("school", StringComparison.Ordinal)
                   && (haystack.Contains("university", StringComparison.Ordinal)
                       || haystack.Contains("/executive-education", StringComparison.Ordinal)
                       || haystack.Contains("business.", StringComparison.Ordinal)));
    }
}
