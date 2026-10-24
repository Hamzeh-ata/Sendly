using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services.MapResearch;

public class MapContactEnrichmentService
{
    private static readonly Regex ContactPathRegex = new(
        @"/(contact|contacts|about|location|locations|impressum)(/|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ISafeHttpFetcher _fetcher;
    private readonly IEnumerable<IWebSearchProvider> _searchProviders;
    private readonly ILocalAiResearchIntelligence _ai;
    private readonly ILocalAiSearchClassifier _searchClassifier;
    private readonly ISendlyDbContext _db;
    private readonly MapOsmContactBootstrapper _osmBootstrapper;
    private readonly ILogger<MapContactEnrichmentService> _logger;

    public MapContactEnrichmentService(
        ISafeHttpFetcher fetcher,
        IEnumerable<IWebSearchProvider> searchProviders,
        ILocalAiResearchIntelligence ai,
        ILocalAiSearchClassifier searchClassifier,
        ISendlyDbContext db,
        MapOsmContactBootstrapper osmBootstrapper,
        ILogger<MapContactEnrichmentService> logger)
    {
        _fetcher = fetcher;
        _searchProviders = searchProviders;
        _ai = ai;
        _searchClassifier = searchClassifier;
        _db = db;
        _osmBootstrapper = osmBootstrapper;
        _logger = logger;
    }

    public async Task EnrichAsync(
        ResearchRun run,
        OutreachProject project,
        Prospect prospect,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        ClearImplausibleContacts(prospect);
        await _osmBootstrapper.BootstrapAsync(prospect, cancellationToken);
        ClearImplausibleContacts(prospect);
        var missing = IdentifyMissingFields(prospect, plan);
        if (missing.Count == 0)
        {
            return;
        }

        var website = prospect.Website;
        if (!MapContactWebsiteGuard.IsPlausibleWebsite(website, prospect.CompanyName))
        {
            website = null;
            if (!missing.Contains("website"))
            {
                missing.Add("website");
            }
        }

        if (string.IsNullOrWhiteSpace(website) && missing.Contains("website"))
        {
            website = await DiscoverWebsiteAsync(prospect, project, plan, cancellationToken);
            if (!string.IsNullOrWhiteSpace(website))
            {
                ApplyField(
                    run,
                    project.Id,
                    prospect,
                    "website",
                    website,
                    website,
                    "web_search",
                    "source_reported",
                    null);
                missing.Remove("website");
            }
        }

        if (!MapContactWebsiteGuard.IsPlausibleWebsite(website, prospect.CompanyName))
        {
            return;
        }

        var urls = await BuildFetchTargetsAsync(website!, cancellationToken);
        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            missing = IdentifyMissingFields(prospect, plan);
            if (missing.Count == 0)
            {
                break;
            }

            await EnrichFromPageAsync(run, project.Id, prospect, url, missing, plan, cancellationToken);
        }
    }

    private static void ClearImplausibleContacts(Prospect prospect)
    {
        if (!string.IsNullOrWhiteSpace(prospect.Website)
            && !MapContactWebsiteGuard.IsPlausibleWebsite(prospect.Website, prospect.CompanyName))
        {
            prospect.Website = null;
            prospect.NormalizedWebsite = null;
        }

        if (!string.IsNullOrWhiteSpace(prospect.Phone)
            && !MapContactWebsiteGuard.IsPlausiblePhone(prospect.Phone))
        {
            prospect.Phone = null;
            prospect.NormalizedPhone = null;
        }

        if (!string.IsNullOrWhiteSpace(prospect.Email)
            && !MapContactWebsiteGuard.IsPlausibleEmail(prospect.Email, prospect.CompanyName, prospect.Website))
        {
            prospect.Email = null;
            prospect.NormalizedEmail = null;
        }
    }

    public static List<string> IdentifyMissingFields(Prospect prospect, MapResearchPlan plan)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(prospect.Website))
        {
            missing.Add("website");
        }

        if (string.IsNullOrWhiteSpace(prospect.Phone))
        {
            missing.Add("phone");
        }

        if (string.IsNullOrWhiteSpace(prospect.Email)
            && RequiresEmail(plan))
        {
            missing.Add("email");
        }
        else if (string.IsNullOrWhiteSpace(prospect.Email))
        {
            missing.Add("email");
        }

        if (string.IsNullOrWhiteSpace(prospect.StreetAddress))
        {
            missing.Add("streetAddress");
        }

        return missing;
    }

    private static bool RequiresEmail(MapResearchPlan plan)
        => plan.Specification.RequirePublicBusinessEmail;

    private async Task<string?> DiscoverWebsiteAsync(
        Prospect prospect,
        OutreachProject project,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        var queries = await BuildSearchQueriesAsync(prospect, project, plan, cancellationToken);
        if (queries.Count == 0)
        {
            return null;
        }

        var hits = await CollectSearchHitsAsync(queries, plan, cancellationToken);
        if (hits.Count == 0)
        {
            return null;
        }

        var ranked = await RankOfficialWebsiteCandidatesAsync(prospect, project, plan, hits, cancellationToken);
        foreach (var candidate in ranked)
        {
            if (MapContactWebsiteGuard.IsPlausibleWebsite(candidate, prospect.CompanyName))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<List<string>> BuildSearchQueriesAsync(
        Prospect prospect,
        OutreachProject project,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        var location = ResolveLocationLabel(prospect, project, plan);
        var industry = ResolveIndustryHint(prospect, project, plan);
        var queries = new List<string>();
        AddUniqueQuery(queries, JoinQueryParts(prospect.CompanyName, location, "official website"));
        AddUniqueQuery(queries, JoinQueryParts(prospect.CompanyName, location, "contact email phone"));
        AddUniqueQuery(queries, JoinQueryParts(prospect.CompanyName, location, industry));
        AddUniqueQuery(queries, JoinQueryParts(prospect.CompanyName, location, "phone"));

        var aiQueries = await ProposeAiContactQueriesAsync(prospect, project, plan, location, cancellationToken);
        foreach (var query in aiQueries)
        {
            AddUniqueQuery(queries, query);
            if (queries.Count >= 6)
            {
                break;
            }
        }

        return queries;
    }

    private static string? ResolveIndustryHint(
        Prospect prospect,
        OutreachProject project,
        MapResearchPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(prospect.Industry))
        {
            return prospect.Industry;
        }

        foreach (var industry in plan.Specification.ApprovedIndustries)
        {
            if (!string.IsNullOrWhiteSpace(industry))
            {
                return industry;
            }
        }

        foreach (var keyword in project.Keywords)
        {
            if (!keyword.IsExcluded && !string.IsNullOrWhiteSpace(keyword.Value))
            {
                return keyword.Value;
            }
        }

        return null;
    }

    private async Task<List<string>> ProposeAiContactQueriesAsync(
        Prospect prospect,
        OutreachProject project,
        MapResearchPlan plan,
        string? location,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _ai.ProposeResearchPlanAsync(
                new ResearchPlanRequest
                {
                    ProjectName = project.Name,
                    ProjectDescription = project.Description,
                    ResearchInstructions =
                        "Find the official website and public contact details for one real business using its exact name and location. Do not invent contacts.",
                    Specification = plan.Specification,
                    ExistingQueries = [],
                    MaxActions = 3,
                    IsFollowUp = true,
                    UnderCoveredLocations = string.IsNullOrWhiteSpace(location) ? [] : [location],
                    OutcomeSummary =
                        $"Missing contacts for '{prospect.CompanyName}' at '{location}'. Need official website, email, and phone."
                },
                cancellationToken);

            var queries = new List<string>();
            foreach (var action in result.Actions)
            {
                if (string.IsNullOrWhiteSpace(action.Query))
                {
                    continue;
                }

                if (!string.Equals(action.ActionType, ResearchActionTypes.BusinessNameWebsiteSearch, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(action.ActionType, ResearchActionTypes.MissingContactSearch, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(action.ActionType, ResearchActionTypes.ExactIndustryLocationSearch, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                queries.Add(action.Query.Trim());
            }

            return queries;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Qwen contact-query planning failed for {Company}", prospect.CompanyName);
            return [];
        }
    }

    private async Task<List<WebSearchHit>> CollectSearchHitsAsync(
        List<string> queries,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        var hits = new List<WebSearchHit>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in queries)
        {
            foreach (var provider in _searchProviders)
            {
                if (!provider.IsEnabled)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var batch = await provider.SearchAsync(
                        new WebSearchRequest
                        {
                            Query = query,
                            MaxResults = 5,
                            Countries = plan.Specification.TargetCountries.ToList(),
                            Country = plan.Specification.TargetCountries.FirstOrDefault()
                        },
                        cancellationToken);
                    foreach (var hit in batch)
                    {
                        if (string.IsNullOrWhiteSpace(hit.Url) || !seenUrls.Add(hit.Url))
                        {
                            continue;
                        }

                        hits.Add(hit);
                        if (hits.Count >= 12)
                        {
                            return hits;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Map contact website search failed for {Provider}", provider.GetType().Name);
                }
            }
        }

        return hits;
    }

    private async Task<List<string>> RankOfficialWebsiteCandidatesAsync(
        Prospect prospect,
        OutreachProject project,
        MapResearchPlan plan,
        List<WebSearchHit> hits,
        CancellationToken cancellationToken)
    {
        var prefiltered = new List<WebSearchHit>();
        foreach (var hit in hits)
        {
            if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                continue;
            }

            if (!MapContactWebsiteGuard.IsPlausibleWebsite(hit.Url, prospect.CompanyName))
            {
                continue;
            }

            if (!MapContactWebsiteGuard.HitMatchesBusiness(
                    prospect.CompanyName,
                    hit.Title,
                    hit.Snippet,
                    hit.Url))
            {
                continue;
            }

            prefiltered.Add(hit);
        }

        if (prefiltered.Count == 0)
        {
            return [];
        }

        var ranked = new List<RankedWebsiteCandidate>();
        var classified = await ClassifyHitsWithQwenAsync(prospect, project, plan, prefiltered, cancellationToken);
        for (var i = 0; i < prefiltered.Count; i++)
        {
            var hit = prefiltered[i];
            if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            var website = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            var item = FindClassification(classified, i);
            var score = ScoreWebsiteCandidate(item);
            if (score <= 0)
            {
                continue;
            }

            ranked.Add(new RankedWebsiteCandidate { Website = website, Score = score });
        }

        if (ranked.Count == 0)
        {
            foreach (var hit in prefiltered)
            {
                if (!MapContactWebsiteGuard.LooksLikeOfficialBusinessSite(
                        prospect.CompanyName,
                        hit.Title,
                        hit.Snippet,
                        hit.Url))
                {
                    continue;
                }

                if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri))
                {
                    continue;
                }

                ranked.Add(new RankedWebsiteCandidate
                {
                    Website = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/'),
                    Score = 10
                });
            }
        }

        return MaterializeRankedWebsites(ranked);
    }

    private static AiBatchSearchClassificationItem? FindClassification(
        List<AiBatchSearchClassificationItem> classified,
        int index)
    {
        foreach (var item in classified)
        {
            if (item.Index == index)
            {
                return item;
            }
        }

        return null;
    }

    private static List<string> MaterializeRankedWebsites(List<RankedWebsiteCandidate> ranked)
    {
        ranked.Sort((left, right) => right.Score.CompareTo(left.Score));
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ranked)
        {
            if (seen.Add(item.Website))
            {
                result.Add(item.Website);
            }
        }

        return result;
    }

    private sealed class RankedWebsiteCandidate
    {
        public string Website { get; set; } = string.Empty;

        public int Score { get; set; }
    }

    private async Task<List<AiBatchSearchClassificationItem>> ClassifyHitsWithQwenAsync(
        Prospect prospect,
        OutreachProject project,
        MapResearchPlan plan,
        List<WebSearchHit> hits,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = new List<AiBatchSearchResultItem>();
            for (var index = 0; index < hits.Count; index++)
            {
                var hit = hits[index];
                results.Add(new AiBatchSearchResultItem
                {
                    Index = index,
                    Title = hit.Title,
                    Snippet = hit.Snippet,
                    Url = hit.Url,
                    Query = prospect.CompanyName
                });
            }

            var request = new AiBatchSearchClassificationRequest
            {
                Results = results,
                Industries = plan.Specification.ApprovedIndustries.ToList(),
                Countries = plan.Specification.TargetCountries.ToList(),
                Locations = BuildLocationHints(prospect, project, plan),
                ExcludedIndustries = plan.Specification.ExcludedIndustries.ToList(),
                ExcludedKeywords = plan.Specification.ExcludedKeywords.ToList()
            };
            var result = await _ai.ClassifySearchResultsBatchAsync(request, cancellationToken);
            return result.Items ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Qwen search classification failed for {Company}", prospect.CompanyName);
            return [];
        }
    }

    private static int ScoreWebsiteCandidate(AiBatchSearchClassificationItem? item)
    {
        if (item is null || !item.IsValid)
        {
            return 0;
        }

        if (string.Equals(item.Decision, "PotentialOfficialBusinessWebsite", StringComparison.OrdinalIgnoreCase))
        {
            return 100 + item.Confidence + (item.CrawlPriority * 5);
        }

        if (string.Equals(item.Decision, "RelevantBusinessCandidate", StringComparison.OrdinalIgnoreCase))
        {
            return 70 + item.Confidence + (item.CrawlPriority * 3);
        }

        if (string.Equals(item.Decision, "PotentialDirectory", StringComparison.OrdinalIgnoreCase))
        {
            return 20 + (item.Confidence / 2);
        }

        return 0;
    }

    private static List<string> BuildLocationHints(
        Prospect prospect,
        OutreachProject project,
        MapResearchPlan plan)
    {
        var locations = new List<string>();
        if (!string.IsNullOrWhiteSpace(prospect.City))
        {
            locations.Add(prospect.City);
        }

        var country = ResolveLocationCountry(prospect, project, plan);
        if (!string.IsNullOrWhiteSpace(country))
        {
            locations.Add(country);
        }

        return locations;
    }

    private static string? ResolveLocationLabel(
        Prospect prospect,
        OutreachProject project,
        MapResearchPlan plan)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(prospect.City))
        {
            parts.Add(prospect.City);
        }

        var country = ResolveLocationCountry(prospect, project, plan);
        if (!string.IsNullOrWhiteSpace(country))
        {
            parts.Add(country);
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static string? ResolveLocationCountry(
        Prospect prospect,
        OutreachProject project,
        MapResearchPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(prospect.Country))
        {
            return prospect.Country;
        }

        foreach (var country in plan.Specification.TargetCountries)
        {
            if (!string.IsNullOrWhiteSpace(country))
            {
                return country;
            }
        }

        foreach (var country in project.Countries)
        {
            if (!string.IsNullOrWhiteSpace(country.Name))
            {
                return country.Name;
            }
        }

        return null;
    }

    private static string JoinQueryParts(params string?[] parts)
    {
        var values = new List<string>();
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                values.Add(part.Trim());
            }
        }

        return string.Join(' ', values);
    }

    private static void AddUniqueQuery(List<string> queries, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var normalized = query.Trim();
        foreach (var existing in queries)
        {
            if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        queries.Add(normalized);
    }

    private async Task<List<string>> BuildFetchTargetsAsync(string website, CancellationToken cancellationToken)
    {
        var urls = new List<string>();
        if (!Uri.TryCreate(website, UriKind.Absolute, out var root)
            && !Uri.TryCreate("https://" + website.Trim(), UriKind.Absolute, out root))
        {
            return urls;
        }

        urls.Add(root.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        var home = await _fetcher.FetchAsync(
            new SafeHttpFetchRequest { Url = root, Timeout = TimeSpan.FromSeconds(20) },
            cancellationToken);
        if (!home.Succeeded || string.IsNullOrWhiteSpace(home.Body))
        {
            return urls;
        }

        var extracted = HtmlPageExtractor.Extract(home.Body, home.FinalUrl ?? root);
        foreach (var link in extracted.SameOriginLinks.Take(20))
        {
            if (!ContactPathRegex.IsMatch(link))
            {
                continue;
            }

            if (!urls.Contains(link, StringComparer.OrdinalIgnoreCase))
            {
                urls.Add(link);
            }

            if (urls.Count >= 3)
            {
                break;
            }
        }

        return urls;
    }

    private async Task EnrichFromPageAsync(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        string url,
        List<string> missing,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        var fetch = await _fetcher.FetchAsync(
            new SafeHttpFetchRequest { Url = uri, Timeout = TimeSpan.FromSeconds(20) },
            cancellationToken);
        if (!fetch.Succeeded || string.IsNullOrWhiteSpace(fetch.Body))
        {
            await AddEvidenceAsync(
                run,
                projectId,
                prospect.Id,
                "map.enrichmentFetchFailed",
                fetch.Error ?? "Fetch failed",
                url,
                "html_extraction",
                "unverified");
            return;
        }

        var pageUrl = fetch.FinalUrl?.ToString() ?? url;
        var extracted = HtmlPageExtractor.Extract(fetch.Body, fetch.FinalUrl ?? uri);
        ApplyDeterministicContacts(run, projectId, prospect, extracted, pageUrl, missing);

        missing = IdentifyMissingFields(prospect, plan);
        if (missing.Count == 0)
        {
            return;
        }

        await ApplyQwenPageVerificationAsync(
            run,
            projectId,
            prospect,
            extracted,
            pageUrl,
            missing,
            plan,
            cancellationToken);

        missing = IdentifyMissingFields(prospect, plan);
        if (missing.Count == 0)
        {
            return;
        }

        await ApplyOllamaContactsAsync(
            run,
            projectId,
            prospect,
            extracted,
            pageUrl,
            missing,
            plan,
            cancellationToken);
    }

    private async Task ApplyQwenPageVerificationAsync(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        ExtractedPageData extracted,
        string pageUrl,
        List<string> missing,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        try
        {
            var verification = await _searchClassifier.VerifyBusinessAsync(
                new AiBusinessVerificationRequest
                {
                    CompanyName = prospect.CompanyName,
                    Url = pageUrl,
                    PageTitle = extracted.Title,
                    PageText = extracted.Text,
                    Industries = plan.Specification.ApprovedIndustries.ToList(),
                    Countries = plan.Specification.TargetCountries.ToList()
                },
                cancellationToken);

            if (!verification.IsValid || !verification.IsRealBusiness)
            {
                return;
            }

            if (missing.Contains("website")
                && !string.IsNullOrWhiteSpace(verification.OfficialWebsite)
                && MapContactWebsiteGuard.IsPlausibleWebsite(verification.OfficialWebsite, prospect.CompanyName))
            {
                ApplyField(
                    run,
                    projectId,
                    prospect,
                    "website",
                    verification.OfficialWebsite,
                    pageUrl,
                    "ollama_assisted",
                    "validated_against_source",
                    verification.OfficialWebsite);
            }

            if (missing.Contains("email")
                && !string.IsNullOrWhiteSpace(verification.Email)
                && IsSupportedByPage(verification.Email, extracted))
            {
                ApplyField(
                    run,
                    projectId,
                    prospect,
                    "email",
                    verification.Email,
                    pageUrl,
                    "ollama_assisted",
                    "validated_against_source",
                    FindExcerpt(verification.Email, extracted.Text));
            }

            if (missing.Contains("phone")
                && !string.IsNullOrWhiteSpace(verification.Phone)
                && IsSupportedByPage(verification.Phone, extracted))
            {
                ApplyField(
                    run,
                    projectId,
                    prospect,
                    "phone",
                    verification.Phone,
                    pageUrl,
                    "ollama_assisted",
                    "validated_against_source",
                    FindExcerpt(verification.Phone, extracted.Text));
            }

            if (!string.IsNullOrWhiteSpace(verification.ContactPage)
                && Uri.TryCreate(verification.ContactPage, UriKind.Absolute, out var contactUri)
                && (contactUri.Scheme is "http" or "https"))
            {
                await EnrichFromContactPageAsync(
                    run,
                    projectId,
                    prospect,
                    verification.ContactPage,
                    plan,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Qwen page verification failed for {Company}", prospect.CompanyName);
        }
    }

    private async Task EnrichFromContactPageAsync(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        string contactPageUrl,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        var missing = IdentifyMissingFields(prospect, plan);
        if (missing.Count == 0)
        {
            return;
        }

        if (!Uri.TryCreate(contactPageUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        var fetch = await _fetcher.FetchAsync(
            new SafeHttpFetchRequest { Url = uri, Timeout = TimeSpan.FromSeconds(20) },
            cancellationToken);
        if (!fetch.Succeeded || string.IsNullOrWhiteSpace(fetch.Body))
        {
            return;
        }

        var pageUrl = fetch.FinalUrl?.ToString() ?? contactPageUrl;
        var extracted = HtmlPageExtractor.Extract(fetch.Body, fetch.FinalUrl ?? uri);
        ApplyDeterministicContacts(run, projectId, prospect, extracted, pageUrl, missing);
    }

    private void ApplyDeterministicContacts(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        ExtractedPageData extracted,
        string pageUrl,
        List<string> missing)
    {
        if (missing.Contains("email"))
        {
            foreach (var email in extracted.Emails)
            {
                if (ApplyField(run, projectId, prospect, "email", email, pageUrl, "html_extraction", "source_reported", email))
                {
                    break;
                }
            }
        }

        if (missing.Contains("phone"))
        {
            foreach (var phone in extracted.Phones)
            {
                if (ApplyField(run, projectId, prospect, "phone", phone, pageUrl, "html_extraction", "source_reported", phone))
                {
                    break;
                }
            }
        }

        foreach (var fact in extracted.Facts)
        {
            if (string.Equals(fact.FactType, "email", StringComparison.OrdinalIgnoreCase)
                && missing.Contains("email")
                && !string.IsNullOrWhiteSpace(fact.FactValue))
            {
                ApplyField(
                    run,
                    projectId,
                    prospect,
                    "email",
                    fact.FactValue,
                    pageUrl,
                    fact.ExtractionMethod ?? "structured_metadata",
                    "source_reported",
                    fact.FactValue);
            }

            if (string.Equals(fact.FactType, "phone", StringComparison.OrdinalIgnoreCase)
                && missing.Contains("phone")
                && !string.IsNullOrWhiteSpace(fact.FactValue))
            {
                ApplyField(
                    run,
                    projectId,
                    prospect,
                    "phone",
                    fact.FactValue,
                    pageUrl,
                    fact.ExtractionMethod ?? "structured_metadata",
                    "source_reported",
                    fact.FactValue);
            }
        }
    }

    private async Task ApplyOllamaContactsAsync(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        ExtractedPageData extracted,
        string pageUrl,
        List<string> missing,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _ai.ExtractBusinessEvidenceAsync(
                new AiEvidenceExtractionRequest
                {
                    CompanyName = prospect.CompanyName,
                    Url = pageUrl,
                    PageTitle = extracted.Title,
                    PageText = extracted.Text,
                    AllowedEmails = extracted.Emails.ToList(),
                    AllowedPhones = extracted.Phones.ToList(),
                    Industries = plan.Specification.ApprovedIndustries.ToList(),
                    Countries = plan.Specification.TargetCountries.ToList(),
                    ExcludedIndustries = plan.Specification.ExcludedIndustries.ToList()
                },
                cancellationToken);

            if (missing.Contains("email")
                && !string.IsNullOrWhiteSpace(result.Email)
                && IsSupportedByPage(result.Email, extracted))
            {
                ApplyField(
                    run,
                    projectId,
                    prospect,
                    "email",
                    result.Email,
                    pageUrl,
                    "ollama_assisted",
                    "validated_against_source",
                    FindExcerpt(result.Email, extracted.Text));
            }
            else if (missing.Contains("email") && !string.IsNullOrWhiteSpace(result.Email))
            {
                await AddEvidenceAsync(
                    run,
                    projectId,
                    prospect.Id,
                    "map.enrichmentRejected",
                    result.Email,
                    pageUrl,
                    "ollama_assisted",
                    "rejected_unsupported_by_source");
            }

            if (missing.Contains("phone")
                && !string.IsNullOrWhiteSpace(result.Phone)
                && IsSupportedByPage(result.Phone, extracted))
            {
                ApplyField(
                    run,
                    projectId,
                    prospect,
                    "phone",
                    result.Phone,
                    pageUrl,
                    "ollama_assisted",
                    "validated_against_source",
                    FindExcerpt(result.Phone, extracted.Text));
            }
            else if (missing.Contains("phone") && !string.IsNullOrWhiteSpace(result.Phone))
            {
                await AddEvidenceAsync(
                    run,
                    projectId,
                    prospect.Id,
                    "map.enrichmentRejected",
                    result.Phone,
                    pageUrl,
                    "ollama_assisted",
                    "rejected_unsupported_by_source");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama-assisted map contact enrichment failed for {Company}", prospect.CompanyName);
        }
    }

    private bool ApplyField(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        string fieldName,
        string value,
        string sourceUrl,
        string extractionMethod,
        string verificationStatus,
        string? excerpt)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(fieldName, "email", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(prospect.Email))
            {
                return false;
            }

            if (!MapContactWebsiteGuard.IsPlausibleEmail(value, prospect.CompanyName, prospect.Website ?? sourceUrl))
            {
                return false;
            }

            prospect.Email = value.Trim();
            prospect.NormalizedEmail = ProspectNormalizer.NormalizeEmail(prospect.Email);
            prospect.AdditionalEmailsJson = ProspectContactStore.MergeAttributedEmails(
                prospect.AdditionalEmailsJson,
                [
                    new AttributedContact
                    {
                        Value = prospect.Email,
                        Channel = "email",
                        SourceUrl = sourceUrl,
                        VerificationStatus = verificationStatus,
                        AttributionReasonCode = extractionMethod,
                        SurroundingText = excerpt,
                        Confidence = extractionMethod == "ollama_assisted" ? 70 : 85
                    }
                ]);
        }
        else if (string.Equals(fieldName, "phone", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(prospect.Phone))
            {
                return false;
            }

            if (!MapContactWebsiteGuard.IsPlausiblePhone(value))
            {
                return false;
            }

            prospect.Phone = value.Trim();
            prospect.NormalizedPhone = ProspectNormalizer.NormalizePhone(prospect.Phone);
            prospect.AdditionalPhonesJson = ProspectContactStore.MergeAttributedPhones(
                prospect.AdditionalPhonesJson,
                [
                    new AttributedContact
                    {
                        Value = prospect.Phone,
                        Channel = "phone",
                        SourceUrl = sourceUrl,
                        VerificationStatus = verificationStatus,
                        AttributionReasonCode = extractionMethod,
                        SurroundingText = excerpt,
                        Confidence = extractionMethod == "ollama_assisted" ? 70 : 85
                    }
                ]);
        }
        else if (string.Equals(fieldName, "website", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(prospect.Website)
                && MapContactWebsiteGuard.IsPlausibleWebsite(prospect.Website, prospect.CompanyName))
            {
                return false;
            }

            if (!MapContactWebsiteGuard.IsPlausibleWebsite(value, prospect.CompanyName))
            {
                return false;
            }

            prospect.Website = value.Trim();
            prospect.NormalizedWebsite = UrlCanonicalizer.ExtractDomain(prospect.Website);
        }
        else
        {
            return false;
        }

        prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
        _db.ResearchEvidenceItems.Add(new ResearchEvidence
        {
            ProjectId = projectId,
            ResearchRunId = run.Id,
            ProspectId = prospect.Id,
            FactType = $"map.{fieldName}",
            FactValue = value.Trim(),
            ConfidenceKind = EvidenceConfidenceKind.Verified,
            SourceUrl = sourceUrl,
            Notes = $"{extractionMethod}; {verificationStatus}; excerpt={Trim(excerpt, 180)}",
            FirstDiscoveredAtUtc = DateTimeOffset.UtcNow,
            LastVerifiedAtUtc = DateTimeOffset.UtcNow
        });
        return true;
    }

    private Task AddEvidenceAsync(
        ResearchRun run,
        Guid projectId,
        Guid prospectId,
        string factType,
        string? value,
        string? sourceUrl,
        string method,
        string status)
    {
        _db.ResearchEvidenceItems.Add(new ResearchEvidence
        {
            ProjectId = projectId,
            ResearchRunId = run.Id,
            ProspectId = prospectId,
            FactType = factType,
            FactValue = value,
            ConfidenceKind = EvidenceConfidenceKind.Inferred,
            SourceUrl = sourceUrl,
            Notes = $"{method}; {status}",
            FirstDiscoveredAtUtc = DateTimeOffset.UtcNow,
            LastVerifiedAtUtc = DateTimeOffset.UtcNow
        });
        return Task.CompletedTask;
    }

    private static bool IsSupportedByPage(string value, ExtractedPageData extracted)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (extracted.Emails.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase))
            || extracted.Phones.Any(x => PhonesMatch(x, normalized)))
        {
            return true;
        }

        var text = extracted.Text ?? string.Empty;
        if (text.Contains(normalized, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        if (digits.Length >= 7)
        {
            var pageDigits = new string(text.Where(char.IsDigit).ToArray());
            return pageDigits.Contains(digits, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool PhonesMatch(string left, string right)
    {
        var a = new string(left.Where(char.IsDigit).ToArray());
        var b = new string(right.Where(char.IsDigit).ToArray());
        return a.Length >= 7 && b.Length >= 7 && (a.EndsWith(b) || b.EndsWith(a));
    }

    private static string? FindExcerpt(string value, string? text)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var index = text.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return value;
        }

        var start = Math.Max(0, index - 40);
        var length = Math.Min(text.Length - start, value.Length + 80);
        return text.Substring(start, length).Trim();
    }

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max];
    }
}
