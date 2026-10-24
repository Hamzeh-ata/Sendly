using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public enum SearchResultDecision
{
    RelevantBusinessCandidate = 0,
    PotentialDirectory = 1,
    PotentialOfficialBusinessWebsite = 2,
    Ambiguous = 3,
    Irrelevant = 4,
    WrongGeography = 5,
    WrongIndustry = 6,
    UnsupportedOrInvalidResult = 7
}

public class SearchResultGateInput
{
    public string? Title { get; set; }
    public string? Snippet { get; set; }
    public string? Url { get; set; }
    public string? Query { get; set; }
    public string? QueryIndustry { get; set; }
    public List<string> Industries { get; set; } = [];
    public List<string> ExcludedIndustries { get; set; } = [];
    public List<string> ExcludedKeywords { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    public List<string> Locations { get; set; } = [];
}

public class SearchResultEvaluation
{
    public SearchResultDecision Decision { get; set; }
    public int Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? IndustryEvidence { get; set; }
    public string? CountryEvidence { get; set; }
    public bool NeedsVerification { get; set; }
    public bool MayFetch => Decision is SearchResultDecision.RelevantBusinessCandidate
        or SearchResultDecision.PotentialDirectory
        or SearchResultDecision.PotentialOfficialBusinessWebsite;
    public bool IsDirectory => Decision == SearchResultDecision.PotentialDirectory;
}

public static class SearchResultGate
{
    private static readonly string[] SocialHosts =
    [
        "facebook.com", "instagram.com", "linkedin.com", "twitter.com", "x.com",
        "youtube.com", "tiktok.com", "pinterest.com", "reddit.com", "snapchat.com"
    ];

    private static readonly string[] AppHosts = ["play.google.com", "apps.apple.com", "microsoftapps.com"];

    private static readonly string[] NewsHosts =
    [
        "cnn.com", "bbc.com", "bbc.co.uk", "nytimes.com", "theguardian.com",
        "reuters.com", "apnews.com", "foxnews.com", "nbcnews.com", "abcnews.go.com"
    ];

    private static readonly string[] DirectoryHosts =
    [
        "yelp.com", "tripadvisor.com", "angi.com", "thumbtack.com", "yellowpages.com",
        "mapquest.com", "chamberofcommerce.com", "bbb.org", "foursquare.com",
        "trustpilot.com"
    ];

    private static readonly string[] DirectoryPathTokens =
    [
        "/directory", "/listings", "/listing/", "/best-", "/top-", "/near-me",
        "/find/", "/search/", "/category/", "/categories/", "/places/"
    ];

    private static readonly string[] NewsPathTokens =
    [
        "/news/", "/article/", "/articles/", "/blog/", "/press-release", "/story/"
    ];

    private static readonly string[] ForumTokens =
    [
        "/forum", "/forums", "/showthread", "/viewtopic", "/member", "/members/",
        "/profile/", "/user/", "/users/", "/community/", "/discussion"
    ];

    private static readonly string[] SoftwareTokens =
    [
        " app download", "download the app", "ios app", "android app",
        "software platform for", "management software", "booking software"
    ];

    private static readonly string[] ForeignCountryNames =
    [
        "Canada", "Mexico", "United Kingdom", "England", "Germany", "France",
        "Australia", "India", "Pakistan", "Saudi Arabia", "United Arab Emirates",
        "Egypt", "Brazil", "Spain", "Italy", "Japan", "China", "South Africa"
    ];

    public static SearchResultEvaluation Evaluate(SearchResultGateInput input)
    {
        if (Uri.TryCreate(input.Url, UriKind.Absolute, out var rawUri))
        {
            var rawHost = rawUri.Host.ToLowerInvariant();
            var rawPath = rawUri.AbsolutePath.ToLowerInvariant();
            if (MatchesHost(rawHost, SocialHosts) || MatchesHost(rawHost, AppHosts))
            {
                return Decision(SearchResultDecision.Irrelevant, 95, "Social platform or app store result cannot be a target business.");
            }

            if (rawHost.StartsWith("forum.", StringComparison.Ordinal)
                || rawHost.StartsWith("forums.", StringComparison.Ordinal)
                || ContainsAny(rawPath, ForumTokens))
            {
                return Decision(SearchResultDecision.Irrelevant, 95, "Forum or user profile result cannot be a target business.");
            }
        }

        var admission = UrlAdmissionPolicy.Evaluate(input.Url, countries: input.Countries, industries: input.Industries);
        if (!admission.Accepted)
        {
            return Decision(SearchResultDecision.UnsupportedOrInvalidResult, 95, admission.Explanation);
        }

        if (!Uri.TryCreate(admission.NormalizedUrl ?? input.Url, UriKind.Absolute, out var uri))
        {
            return Decision(SearchResultDecision.UnsupportedOrInvalidResult, 95, "URL could not be parsed.");
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();
        var evidence = $"{input.Title} {input.Snippet}";
        var fullEvidence = $"{evidence} {uri}";

        if (MatchesHost(host, SocialHosts) || MatchesHost(host, AppHosts))
        {
            return Decision(SearchResultDecision.Irrelevant, 95, "Social platform or app store result cannot be a target business.");
        }

        if (host.StartsWith("forum.", StringComparison.Ordinal) || ContainsAny(path, ForumTokens))
        {
            return Decision(SearchResultDecision.Irrelevant, 95, "Forum or user profile result cannot be a target business.");
        }

        if (MatchesHost(host, NewsHosts) || ContainsAny(path, NewsPathTokens))
        {
            return Decision(SearchResultDecision.Irrelevant, 90, "News or article result is not a business candidate.");
        }

        if (ContainsAny(evidence.ToLowerInvariant(), SoftwareTokens))
        {
            return Decision(SearchResultDecision.Irrelevant, 85, "Software application result does not operate the target venue or service.");
        }

        var industry = IndustryEvidenceClassifier.Classify(
            input.Title,
            input.Title,
            input.Snippet,
            input.Url,
            input.Industries,
            MergeExclusions(input));

        var foreign = FindForeignCountry(fullEvidence, input.Countries);
        var geo = EvaluateGeography(fullEvidence, input, uri);
        var isDirectory = IsDirectory(uri, host, path, admission.PagePurpose);
        if (isDirectory)
        {
            if (industry.IsExcluded
                && string.Equals(industry.ReasonCode, "exclusion.configured_term", StringComparison.OrdinalIgnoreCase))
            {
                return Decision(SearchResultDecision.WrongIndustry, 90, industry.Explanation);
            }

            if (foreign is not null && geo.CountryEvidence is null)
            {
                return Decision(SearchResultDecision.WrongGeography, 85, $"Evidence points to {foreign}, outside the target geography.");
            }

            if (!industry.MatchedApprovedIndustry && !QueryMatchesIndustry(input) && !HasDirectIndustryTerm($"{evidence} {input.Query}", input))
            {
                return Decision(SearchResultDecision.Ambiguous, 45, "Directory result has no matching industry evidence yet.", true);
            }

            return new SearchResultEvaluation
            {
                Decision = SearchResultDecision.PotentialDirectory,
                Confidence = industry.MatchedApprovedIndustry ? 75 : 55,
                Reason = "Directory or listing source may reveal target businesses; it is not itself a prospect.",
                IndustryEvidence = industry.MatchedApprovedIndustry ? industry.Explanation : "Query industry matched a directory result.",
                CountryEvidence = geo.CountryEvidence,
                NeedsVerification = true
            };
        }

        if (industry.IsExcluded)
        {
            return Decision(SearchResultDecision.WrongIndustry, 90, industry.Explanation);
        }

        if (foreign is not null && geo.CountryEvidence is null)
        {
            return Decision(SearchResultDecision.WrongGeography, 85, $"Evidence points to {foreign}, outside the target geography.");
        }

        if (geo.IsMismatch)
        {
            return Decision(SearchResultDecision.WrongGeography, 85, geo.Reason);
        }

        if (!industry.MatchedApprovedIndustry)
        {
            if (QueryMatchesIndustry(input) && HasBusinessSignal(evidence))
            {
                return new SearchResultEvaluation
                {
                    Decision = SearchResultDecision.Ambiguous,
                    Confidence = 45,
                    Reason = "Search query matches the target but title and snippet do not independently prove the industry.",
                    CountryEvidence = geo.CountryEvidence,
                    NeedsVerification = true
                };
            }

            return Decision(SearchResultDecision.WrongIndustry, 80, industry.Explanation);
        }

        if (geo.CountryEvidence is null && input.Countries.Count > 0)
        {
            return new SearchResultEvaluation
            {
                Decision = SearchResultDecision.Ambiguous,
                Confidence = 55,
                Reason = "Industry evidence matches but operating-country evidence is missing from the search result.",
                IndustryEvidence = industry.Explanation,
                NeedsVerification = true
            };
        }

        var isOfficial = admission.PagePurpose == UrlPagePurpose.CompanyProfile
                         || path is "/" or "";
        return new SearchResultEvaluation
        {
            Decision = isOfficial
                ? SearchResultDecision.PotentialOfficialBusinessWebsite
                : SearchResultDecision.RelevantBusinessCandidate,
            Confidence = 80,
            Reason = "Title, snippet, URL, industry and geography evidence align with the target.",
            IndustryEvidence = industry.Explanation,
            CountryEvidence = geo.CountryEvidence,
            NeedsVerification = true
        };
    }

    public static bool IsDirectoryUrl(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();
        return IsDirectory(uri, host, path, UrlAdmissionPolicy.ClassifyPagePurpose(uri.ToString()));
    }

    private static bool IsDirectory(Uri uri, string host, string path, UrlPagePurpose purpose)
    {
        if (ResearchDirectoryCatalog.IsDirectoryUrl(uri) || ResearchDirectoryCatalog.IsDirectoryHost(uri.ToString()))
        {
            return true;
        }

        if (purpose == UrlPagePurpose.DirectoryListing)
        {
            return true;
        }

        if (MatchesHost(host, DirectoryHosts))
        {
            return true;
        }

        return host.Contains("directory", StringComparison.Ordinal) && ContainsAny(path, DirectoryPathTokens);
    }

    private static GeographyResult EvaluateGeography(string evidence, SearchResultGateInput input, Uri uri)
    {
        if (input.Countries.Count == 0)
        {
            return new GeographyResult(null, false, string.Empty);
        }

        if (CountryScopeHelper.TextMentionsCountry(evidence, input.Countries))
        {
            return new GeographyResult("Target country is named in the search evidence.", false, string.Empty);
        }

        foreach (var location in input.Locations)
        {
            if (!string.IsNullOrWhiteSpace(location)
                && evidence.Contains(location, StringComparison.OrdinalIgnoreCase))
            {
                return new GeographyResult($"Target location '{location}' is named in the search evidence.", false, string.Empty);
            }
        }

        var inferred = CountryScopeHelper.InferCountryFromHost(uri.Host)
                       ?? CountryScopeHelper.InferCountryFromPath(uri.AbsolutePath);
        if (inferred is not null)
        {
            if (CountryScopeHelper.CountryMatches(inferred, input.Countries))
            {
                return new GeographyResult($"Host or path indicates {inferred}.", false, string.Empty);
            }

            return new GeographyResult(null, true, $"Host or path indicates {inferred}, outside the target geography.");
        }

        foreach (var state in UsStateNames)
        {
            if (ContainsWord(evidence, state) && IsUnitedStatesTarget(input.Countries))
            {
                return new GeographyResult($"US state '{state}' is named in the search evidence.", false, string.Empty);
            }
        }

        return new GeographyResult(null, false, string.Empty);
    }

    private static string? FindForeignCountry(string evidence, List<string> targets)
    {
        foreach (var country in ForeignCountryNames)
        {
            if (targets.Any(x => CountryScopeHelper.AreSameCountry(x, country)))
            {
                continue;
            }

            if (ContainsWord(evidence, country))
            {
                return country;
            }
        }

        return null;
    }

    private static bool IsUnitedStatesTarget(List<string> countries)
        => countries.Any(x => CountryScopeHelper.AreSameCountry(x, "United States"));

    private static bool QueryMatchesIndustry(SearchResultGateInput input)
    {
        var query = $"{input.Query} {input.QueryIndustry}";
        if (string.IsNullOrWhiteSpace(query.Trim()))
        {
            return false;
        }

        var classification = IndustryEvidenceClassifier.Classify(query, query, null, null, input.Industries, MergeExclusions(input));
        return classification.MatchedApprovedIndustry;
    }

    private static bool HasDirectIndustryTerm(string text, SearchResultGateInput input)
    {
        foreach (var industry in input.Industries)
        {
            if (string.IsNullOrWhiteSpace(industry))
            {
                continue;
            }

            if (text.Contains(industry, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(IndustrySynonymCatalog.MatchIndustry(text, [industry])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBusinessSignal(string evidence)
    {
        var lower = evidence.ToLowerInvariant();
        var signals = new[]
        {
            "official website", "book now", "booking", "visit us", "our location",
            "locations", "contact us", "hours", "tickets", "parties", "arena",
            "field", "park", "center", "centre", "venue", "facility"
        };
        return ContainsAny(lower, signals);
    }

    private static List<string> MergeExclusions(SearchResultGateInput input)
    {
        var result = new List<string>(input.ExcludedIndustries);
        result.AddRange(input.ExcludedKeywords);
        return result;
    }

    private static SearchResultEvaluation Decision(SearchResultDecision decision, int confidence, string reason, bool needsVerification = false)
        => new()
        {
            Decision = decision,
            Confidence = confidence,
            Reason = reason,
            NeedsVerification = needsVerification
        };

    private static bool MatchesHost(string host, IEnumerable<string> hosts)
    {
        foreach (var candidate in hosts)
        {
            if (host.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + word.Length;
            var afterOk = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static readonly string[] UsStateNames =
    [
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut",
        "Delaware", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa",
        "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan",
        "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada",
        "New Hampshire", "New Jersey", "New Mexico", "New York", "North Carolina",
        "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island",
        "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont",
        "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming"
    ];

    private sealed class GeographyResult
    {
        public GeographyResult(string? countryEvidence, bool isMismatch, string reason)
        {
            CountryEvidence = countryEvidence;
            IsMismatch = isMismatch;
            Reason = reason;
        }

        public string? CountryEvidence { get; }
        public bool IsMismatch { get; }
        public string Reason { get; }
    }
}
