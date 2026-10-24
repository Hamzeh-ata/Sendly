using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services.MapResearch;

public class MapOpenAiContactExtractionService
{
    private static readonly Regex ContactPathRegex = new(
        @"/(contact|contacts|about|location|locations|impressum)(/|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ISendlyDbContext _db;
    private readonly IOpenAiMapContactAssistant _openAi;
    private readonly ISafeHttpFetcher _fetcher;
    private readonly IEnumerable<IWebSearchProvider> _searchProviders;
    private readonly MapResearchPlanBuilder _planBuilder;
    private readonly ILogger<MapOpenAiContactExtractionService> _logger;

    public MapOpenAiContactExtractionService(
        ISendlyDbContext db,
        IOpenAiMapContactAssistant openAi,
        ISafeHttpFetcher fetcher,
        IEnumerable<IWebSearchProvider> searchProviders,
        MapResearchPlanBuilder planBuilder,
        ILogger<MapOpenAiContactExtractionService> logger)
    {
        _db = db;
        _openAi = openAi;
        _fetcher = fetcher;
        _searchProviders = searchProviders;
        _planBuilder = planBuilder;
        _logger = logger;
    }

    public async Task<MapOpenAiContactExtractResultDto> ExtractForProspectAsync(
        Guid projectId,
        Guid prospectId,
        CancellationToken cancellationToken)
    {
        if (!_openAi.IsConfigured)
        {
            throw new InvalidOperationException("OpenAI is not configured.");
        }

        var project = await LoadProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        var prospect = await _db.Prospects
            .FirstOrDefaultAsync(
                x => x.Id == prospectId && x.ProjectId == projectId,
                cancellationToken);
        if (prospect is null)
        {
            throw new InvalidOperationException("Prospect was not found.");
        }

        var run = await ResolveRunAsync(projectId, prospect.ResearchRunId, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("No map research run was found for this prospect.");
        }

        var plan = await BuildPlanAsync(project, cancellationToken);
        var before = SnapshotContacts(prospect);
        await ExtractIntoProspectAsync(run, project, prospect, plan, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return BuildResult(prospect, before);
    }

    public async Task<MapOpenAiContactBatchResultDto> ExtractMissingAsync(
        Guid projectId,
        Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        if (!_openAi.IsConfigured)
        {
            throw new InvalidOperationException("OpenAI is not configured.");
        }

        var project = await LoadProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project was not found.");
        }

        var run = await ResolveRunAsync(projectId, researchRunId, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("No map research run was found for this project.");
        }

        var plan = await BuildPlanAsync(project, cancellationToken);
        var prospects = await _db.Prospects
            .Where(x => x.ProjectId == projectId
                        && x.ResearchRunId == run.Id
                        && x.Latitude != null
                        && x.Longitude != null
                        && x.Status != ProspectStatus.Rejected)
            .ToListAsync(cancellationToken);

        var missing = new List<Prospect>();
        foreach (var prospect in prospects)
        {
            if (HasMissingContacts(prospect))
            {
                missing.Add(prospect);
            }
        }

        missing.Sort((left, right) => right.UpdatedAtUtc.CompareTo(left.UpdatedAtUtc));
        if (missing.Count > 40)
        {
            missing = missing.GetRange(0, 40);
        }

        var batch = new MapOpenAiContactBatchResultDto
        {
            ResearchRunId = run.Id
        };

        foreach (var prospect in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = SnapshotContacts(prospect);
            try
            {
                await ExtractIntoProspectAsync(run, project, prospect, plan, cancellationToken);
                var result = BuildResult(prospect, before);
                batch.Results.Add(result);
                batch.Processed++;
                if (result.FilledFields.Count > 0)
                {
                    batch.Improved++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenAI extract failed for prospect {ProspectId}", prospect.Id);
                batch.Results.Add(new MapOpenAiContactExtractResultDto
                {
                    ProspectId = prospect.Id,
                    CompanyName = prospect.CompanyName,
                    Success = false,
                    Message = ex.Message
                });
                batch.Processed++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return batch;
    }

    private async Task ExtractIntoProspectAsync(
        ResearchRun run,
        OutreachProject project,
        Prospect prospect,
        MapResearchPlan plan,
        CancellationToken cancellationToken)
    {
        var missing = IdentifyMissingFields(prospect);
        if (missing.Count == 0)
        {
            return;
        }

        var hint = await _openAi.SuggestContactTargetsAsync(
            new OpenAiMapContactHintRequest
            {
                CompanyName = prospect.CompanyName,
                City = prospect.City,
                Country = prospect.Country,
                Industry = prospect.Industry,
                Website = prospect.Website,
                StreetAddress = prospect.StreetAddress,
                MissingFields = missing,
                TargetCountries = plan.Specification.TargetCountries.ToList()
            },
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(hint.ErrorSummary) && string.IsNullOrWhiteSpace(hint.OfficialWebsite))
        {
            _logger.LogDebug("OpenAI hint error for {Company}: {Error}", prospect.CompanyName, hint.ErrorSummary);
        }

        var targets = new List<string>();
        AddWebsiteTarget(targets, hint.OfficialWebsite, prospect.CompanyName);
        AddWebsiteTarget(targets, hint.ContactPageUrl, prospect.CompanyName);
        AddWebsiteTarget(targets, prospect.Website, prospect.CompanyName);

        var searchTargets = await DiscoverFromQueriesAsync(
            prospect,
            plan,
            hint.SearchQueries,
            cancellationToken);
        foreach (var target in searchTargets)
        {
            AddWebsiteTarget(targets, target, prospect.CompanyName);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            missing = IdentifyMissingFields(prospect);
            if (missing.Count == 0)
            {
                break;
            }

            await EnrichFromUrlAsync(run, project.Id, prospect, target, missing, visited, 0, cancellationToken);
        }
    }

    private async Task EnrichFromUrlAsync(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        string url,
        List<string> missing,
        HashSet<string> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > 2)
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        var key = uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
        if (!visited.Add(key))
        {
            return;
        }

        var fetch = await _fetcher.FetchAsync(
            new SafeHttpFetchRequest { Url = uri, Timeout = TimeSpan.FromSeconds(25) },
            cancellationToken);
        if (!fetch.Succeeded || string.IsNullOrWhiteSpace(fetch.Body))
        {
            return;
        }

        var pageUrl = fetch.FinalUrl?.ToString() ?? url;
        var extracted = HtmlPageExtractor.Extract(fetch.Body, fetch.FinalUrl ?? uri);
        ApplyDeterministic(run, projectId, prospect, extracted, pageUrl, missing);

        if (string.IsNullOrWhiteSpace(prospect.Website)
            && MapContactWebsiteGuard.IsPlausibleWebsite(pageUrl, prospect.CompanyName))
        {
            ApplyWebsite(run, projectId, prospect, NormalizeWebsite(pageUrl), pageUrl);
        }

        missing = IdentifyMissingFields(prospect);
        if (missing.Count == 0)
        {
            return;
        }

        var allowedEmails = new List<string>();
        foreach (var email in extracted.Emails)
        {
            allowedEmails.Add(email);
        }

        var allowedPhones = new List<string>();
        foreach (var phone in extracted.Phones)
        {
            allowedPhones.Add(phone);
        }

        var ai = await _openAi.ExtractContactsFromPageAsync(
            new OpenAiMapPageContactRequest
            {
                CompanyName = prospect.CompanyName,
                PageUrl = pageUrl,
                PageTitle = extracted.Title,
                PageText = extracted.Text,
                AllowedEmails = allowedEmails,
                AllowedPhones = allowedPhones,
                MissingFields = missing
            },
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(ai.Email) && missing.Contains("email"))
        {
            ApplyEmail(run, projectId, prospect, ai.Email, pageUrl);
        }

        if (!string.IsNullOrWhiteSpace(ai.Phone) && missing.Contains("phone"))
        {
            ApplyPhone(run, projectId, prospect, ai.Phone, pageUrl);
        }

        if (!string.IsNullOrWhiteSpace(ai.Website)
            && string.IsNullOrWhiteSpace(prospect.Website)
            && MapContactWebsiteGuard.IsPlausibleWebsite(ai.Website, prospect.CompanyName))
        {
            ApplyWebsite(run, projectId, prospect, NormalizeWebsite(ai.Website), pageUrl);
        }

        if (!string.IsNullOrWhiteSpace(ai.ContactPageUrl)
            && !string.Equals(ai.ContactPageUrl, pageUrl, StringComparison.OrdinalIgnoreCase))
        {
            missing = IdentifyMissingFields(prospect);
            if (missing.Count > 0)
            {
                await EnrichFromUrlAsync(
                    run,
                    projectId,
                    prospect,
                    ai.ContactPageUrl,
                    missing,
                    visited,
                    depth + 1,
                    cancellationToken);
            }
        }
        else
        {
            await TryContactLinksAsync(run, projectId, prospect, extracted, visited, depth, cancellationToken);
        }
    }

    private async Task TryContactLinksAsync(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        ExtractedPageData extracted,
        HashSet<string> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        var missing = IdentifyMissingFields(prospect);
        if (missing.Count == 0)
        {
            return;
        }

        var links = new List<string>();
        foreach (var link in extracted.SameOriginLinks)
        {
            if (!ContactPathRegex.IsMatch(link))
            {
                continue;
            }

            links.Add(link);
            if (links.Count >= 2)
            {
                break;
            }
        }

        foreach (var link in links)
        {
            missing = IdentifyMissingFields(prospect);
            if (missing.Count == 0)
            {
                break;
            }

            await EnrichFromUrlAsync(run, projectId, prospect, link, missing, visited, depth + 1, cancellationToken);
        }
    }

    private async Task<List<string>> DiscoverFromQueriesAsync(
        Prospect prospect,
        MapResearchPlan plan,
        List<string> queries,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        var allQueries = new List<string>();
        foreach (var query in queries)
        {
            if (!string.IsNullOrWhiteSpace(query))
            {
                allQueries.Add(query.Trim());
            }
        }

        if (allQueries.Count == 0)
        {
            allQueries.Add($"{prospect.CompanyName} {prospect.City} {prospect.Country} official website");
            allQueries.Add($"{prospect.CompanyName} {prospect.City} contact email phone");
        }

        if (allQueries.Count > 4)
        {
            allQueries = allQueries.GetRange(0, 4);
        }

        foreach (var query in allQueries)
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
                    var country = ResolveFirstCountry(plan);
                    var countries = new List<string>();
                    foreach (var item in plan.Specification.TargetCountries)
                    {
                        countries.Add(item);
                    }

                    var hits = await provider.SearchAsync(
                        new WebSearchRequest
                        {
                            Query = query,
                            MaxResults = 5,
                            Countries = countries,
                            Country = country
                        },
                        cancellationToken);
                    foreach (var hit in hits)
                    {
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

                        if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri))
                        {
                            continue;
                        }

                        result.Add(uri.GetLeftPart(UriPartial.Authority).TrimEnd('/'));
                        if (result.Count >= 5)
                        {
                            return result;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "OpenAI-backed search failed for provider {Provider}", provider.GetType().Name);
                }
            }
        }

        return result;
    }

    private void ApplyDeterministic(
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
                if (ApplyEmail(run, projectId, prospect, email, pageUrl))
                {
                    break;
                }
            }
        }

        if (missing.Contains("phone"))
        {
            foreach (var phone in extracted.Phones)
            {
                if (ApplyPhone(run, projectId, prospect, phone, pageUrl))
                {
                    break;
                }
            }
        }
    }

    private bool ApplyEmail(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        string email,
        string sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(prospect.Email))
        {
            return false;
        }

        if (!MapContactWebsiteGuard.IsPlausibleEmail(email, prospect.CompanyName, prospect.Website ?? sourceUrl))
        {
            return false;
        }

        prospect.Email = email.Trim();
        prospect.NormalizedEmail = ProspectNormalizer.NormalizeEmail(prospect.Email);
        prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddEvidence(run, projectId, prospect.Id, "map.email", prospect.Email, sourceUrl, "openai_assisted");
        return true;
    }

    private bool ApplyPhone(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        string phone,
        string sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(prospect.Phone))
        {
            return false;
        }

        if (!MapContactWebsiteGuard.IsPlausiblePhone(phone))
        {
            return false;
        }

        prospect.Phone = phone.Trim();
        prospect.NormalizedPhone = ProspectNormalizer.NormalizePhone(prospect.Phone);
        prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddEvidence(run, projectId, prospect.Id, "map.phone", prospect.Phone, sourceUrl, "openai_assisted");
        return true;
    }

    private void ApplyWebsite(
        ResearchRun run,
        Guid projectId,
        Prospect prospect,
        string website,
        string sourceUrl)
    {
        prospect.Website = website;
        prospect.NormalizedWebsite = UrlCanonicalizer.ExtractDomain(website);
        prospect.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddEvidence(run, projectId, prospect.Id, "map.website", website, sourceUrl, "openai_assisted");
    }

    private void AddEvidence(
        ResearchRun run,
        Guid projectId,
        Guid prospectId,
        string factType,
        string value,
        string sourceUrl,
        string method)
    {
        _db.ResearchEvidenceItems.Add(new ResearchEvidence
        {
            ProjectId = projectId,
            ResearchRunId = run.Id,
            ProspectId = prospectId,
            FactType = factType,
            FactValue = value,
            ConfidenceKind = EvidenceConfidenceKind.Verified,
            SourceUrl = sourceUrl,
            Notes = method,
            FirstDiscoveredAtUtc = DateTimeOffset.UtcNow,
            LastVerifiedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static void AddWebsiteTarget(List<string> targets, string? website, string companyName)
    {
        if (string.IsNullOrWhiteSpace(website))
        {
            return;
        }

        if (!MapContactWebsiteGuard.IsPlausibleWebsite(website, companyName))
        {
            return;
        }

        var normalized = NormalizeWebsite(website);
        foreach (var existing in targets)
        {
            if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing, website, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        targets.Add(website.Trim());
    }

    private static string NormalizeWebsite(string website)
    {
        if (!Uri.TryCreate(website, UriKind.Absolute, out var uri)
            && !Uri.TryCreate("https://" + website.Trim(), UriKind.Absolute, out uri))
        {
            return website.Trim();
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static bool HasMissingContacts(Prospect prospect)
        => string.IsNullOrWhiteSpace(prospect.Email)
           || string.IsNullOrWhiteSpace(prospect.Phone)
           || string.IsNullOrWhiteSpace(prospect.Website);

    private static List<string> IdentifyMissingFields(Prospect prospect)
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

        if (string.IsNullOrWhiteSpace(prospect.Email))
        {
            missing.Add("email");
        }

        return missing;
    }

    private static ContactSnapshot SnapshotContacts(Prospect prospect)
        => new(
            prospect.Email,
            prospect.Phone,
            prospect.Website);

    private static MapOpenAiContactExtractResultDto BuildResult(Prospect prospect, ContactSnapshot before)
    {
        var filled = new List<string>();
        if (string.IsNullOrWhiteSpace(before.Email) && !string.IsNullOrWhiteSpace(prospect.Email))
        {
            filled.Add("email");
        }

        if (string.IsNullOrWhiteSpace(before.Phone) && !string.IsNullOrWhiteSpace(prospect.Phone))
        {
            filled.Add("phone");
        }

        if (string.IsNullOrWhiteSpace(before.Website) && !string.IsNullOrWhiteSpace(prospect.Website))
        {
            filled.Add("website");
        }

        return new MapOpenAiContactExtractResultDto
        {
            ProspectId = prospect.Id,
            CompanyName = prospect.CompanyName,
            Success = true,
            Email = prospect.Email,
            Phone = prospect.Phone,
            Website = prospect.Website,
            FilledFields = filled,
            Message = filled.Count == 0
                ? "No new contacts found."
                : $"Filled: {string.Join(", ", filled)}"
        };
    }

    private async Task<MapResearchPlan> BuildPlanAsync(
        OutreachProject project,
        CancellationToken cancellationToken)
    {
        var request = new ProjectUnderstandingRequest
        {
            ProjectName = project.Name,
            Description = project.Description,
            ResearchInstructions = project.ResearchInstructions,
            ApprovedIndustries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Countries = project.Countries.Select(x => x.Name).ToList(),
            TargetRoles = project.TargetRoles.Select(x => x.Role).ToList(),
            MinQualificationScore = project.MinQualificationScore,
            TargetProspectCount = project.DefaultRequestedProspectCount,
            QueryRequireOfficialWebsite = false,
            QueryRequirePublicBusinessEmail = false,
            Budgets = new ResearchBudgetSpec
            {
                MaxSearchRequests = 20,
                MaxUrlsPerRun = 50
            }
        };
        var spec = ProjectUnderstandingEngine.Interpret(request);
        return await _planBuilder.BuildAsync(project, spec, request.Budgets!, cancellationToken);
    }

    private async Task<OutreachProject?> LoadProjectAsync(Guid projectId, CancellationToken cancellationToken)
        => await _db.OutreachProjects
            .Include(x => x.Countries)
            .Include(x => x.Industries)
            .Include(x => x.Keywords)
            .Include(x => x.TargetRoles)
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);

    private async Task<ResearchRun?> ResolveRunAsync(
        Guid projectId,
        Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        if (researchRunId is not null)
        {
            return await _db.ResearchRuns
                .FirstOrDefaultAsync(
                    x => x.Id == researchRunId.Value && x.ProjectId == projectId && x.Mode == ResearchMode.Map,
                    cancellationToken);
        }

        return await _db.ResearchRuns
            .Where(x => x.ProjectId == projectId && x.Mode == ResearchMode.Map)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? ResolveFirstCountry(MapResearchPlan plan)
    {
        foreach (var country in plan.Specification.TargetCountries)
        {
            if (!string.IsNullOrWhiteSpace(country))
            {
                return country;
            }
        }

        return null;
    }

    private sealed record ContactSnapshot(string? Email, string? Phone, string? Website);
}
