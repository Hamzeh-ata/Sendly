using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class ResearchDiagnosticsService
{
    private readonly ISendlyDbContext _db;
    private readonly ResearchRunService _researchRunService;

    public ResearchDiagnosticsService(ISendlyDbContext db, ResearchRunService researchRunService)
    {
        _db = db;
        _researchRunService = researchRunService;
    }

    public async Task<List<DiscoveredUrlDto>> ListDiscoveredUrlsAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        var items = await _db.DiscoveredUrls.AsNoTracking()
            .Where(x => x.ResearchRunId == researchRunId)
            .ToListAsync(cancellationToken);
        items = items.OrderByDescending(x => x.DiscoveredAtUtc).Take(500).ToList();

        var parentIds = items
            .Where(x => x.ParentDiscoveredUrlId.HasValue)
            .Select(x => x.ParentDiscoveredUrlId!.Value)
            .Distinct()
            .ToList();
        var parentUrls = new Dictionary<Guid, string>();
        if (parentIds.Count > 0)
        {
            var parents = await _db.DiscoveredUrls.AsNoTracking()
                .Where(x => parentIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Url })
                .ToListAsync(cancellationToken);
            foreach (var parent in parents)
            {
                parentUrls[parent.Id] = parent.Url;
            }
        }

        return items.Select(x => MapDiscoveredUrl(x, parentUrls)).ToList();
    }

    public async Task<List<CrawledPageDto>> ListCrawledPagesAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        var items = await _db.CrawledPages.AsNoTracking()
            .Where(x => x.ResearchRunId == researchRunId)
            .ToListAsync(cancellationToken);
        items = items.OrderByDescending(x => x.CrawledAtUtc).Take(500).ToList();

        return items.Select(x => MapCrawledPage(x, includeExcerpt: false)).ToList();
    }

    public async Task<CrawledPageDto?> GetCrawledPageAsync(
        Guid researchRunId,
        Guid crawledPageId,
        CancellationToken cancellationToken)
    {
        var item = await _db.CrawledPages.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ResearchRunId == researchRunId && x.Id == crawledPageId,
                cancellationToken);
        return item is null ? null : MapCrawledPage(item, includeExcerpt: true);
    }

    public async Task<List<ResearchRunEventDto>> ListEventsAsync(
        Guid researchRunId,
        CancellationToken cancellationToken,
        string? severity = null,
        string? stage = null,
        string? eventType = null,
        string? search = null,
        Guid? afterId = null,
        int take = 200)
    {
        var limit = Math.Clamp(take, 1, 500);
        var query = _db.ResearchRunEvents.AsNoTracking()
            .Where(x => x.ResearchRunId == researchRunId);

        if (!string.IsNullOrWhiteSpace(severity)
            && Enum.TryParse<ResearchRunEventSeverity>(severity, true, out var severityValue))
        {
            query = query.Where(x => x.Severity == severityValue);
        }

        if (!string.IsNullOrWhiteSpace(stage)
            && Enum.TryParse<ResearchPipelineStage>(stage, true, out var stageValue))
        {
            query = query.Where(x => x.Stage == stageValue);
        }

        if (!string.IsNullOrWhiteSpace(eventType)
            && Enum.TryParse<ResearchRunEventType>(eventType, true, out var eventTypeValue))
        {
            query = query.Where(x => x.EventType == eventTypeValue);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                (x.Message != null && x.Message.Contains(term))
                || (x.Url != null && x.Url.Contains(term))
                || (x.Domain != null && x.Domain.Contains(term))
                || (x.Provider != null && x.Provider.Contains(term)));
        }

        var items = await query.ToListAsync(cancellationToken);

        if (afterId.HasValue)
        {
            var after = items.FirstOrDefault(x => x.Id == afterId.Value);
            if (after is not null)
            {
                items = items
                    .Where(x => x.CreatedAtUtc > after.CreatedAtUtc
                        || (x.CreatedAtUtc == after.CreatedAtUtc && x.Id.CompareTo(after.Id) > 0))
                    .ToList();
            }
        }

        items = items
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(limit)
            .ToList();

        return items.Select(ResearchEventRecorder.Map).ToList();
    }

    public async Task<List<ResearchErrorGroupDto>> ListErrorsAsync(
        Guid? researchRunId,
        CancellationToken cancellationToken)
    {
        var query = _db.ResearchRunEvents.AsNoTracking()
            .Where(x => x.Severity == ResearchRunEventSeverity.Error
                || x.Severity == ResearchRunEventSeverity.Critical
                || x.EventType == ResearchRunEventType.Error
                || x.EventType == ResearchRunEventType.FetchFailed
                || x.EventType == ResearchRunEventType.RobotsDenied
                || x.EventType == ResearchRunEventType.SsrfBlocked
                || x.EventType == ResearchRunEventType.TimeoutOccurred
                || x.EventType == ResearchRunEventType.RateLimited);

        if (researchRunId.HasValue)
        {
            query = query.Where(x => x.ResearchRunId == researchRunId.Value);
        }

        var items = await query.ToListAsync(cancellationToken);
        items = items
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(1000)
            .ToList();

        return GroupErrors(items);
    }

    public async Task<ResearchOverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var runs = await _db.ResearchRuns.AsNoTracking().ToListAsync(cancellationToken);
        var overview = new ResearchOverviewDto
        {
            ActiveRuns = runs.Count(x => x.Status == ResearchRunStatus.Running),
            QueuedRuns = runs.Count(x => x.Status == ResearchRunStatus.Pending),
            CompletedRuns = runs.Count(x =>
                x.Status is ResearchRunStatus.Completed or ResearchRunStatus.CompletedWithFailures),
            FailedRuns = runs.Count(x => x.Status == ResearchRunStatus.Failed),
            CancelledRuns = runs.Count(x => x.Status == ResearchRunStatus.Cancelled),
            PagesFetched = runs.Sum(x => x.PagesCrawled),
            UrlsDiscovered = runs.Sum(x => x.UrlsDiscovered),
            CompaniesFound = runs.Sum(x => x.CompaniesFound),
            ContactsFound = runs.Sum(x => x.ContactsFound),
            QualifiedProspects = runs.Sum(x => x.QualifiedCount),
            CrawlErrors = runs.Sum(x => x.CrawlErrors)
        };

        overview.DomainsDiscovered = await _db.DiscoveredUrls.AsNoTracking()
            .Where(x => x.NormalizedDomain != null)
            .Select(x => x.NormalizedDomain!)
            .Distinct()
            .CountAsync(cancellationToken);

        overview.EventErrors = await _db.ResearchRunEvents.AsNoTracking()
            .CountAsync(
                x => x.Severity == ResearchRunEventSeverity.Error
                    || x.Severity == ResearchRunEventSeverity.Critical,
                cancellationToken);
        overview.EventWarnings = await _db.ResearchRunEvents.AsNoTracking()
            .CountAsync(x => x.Severity == ResearchRunEventSeverity.Warning, cancellationToken);

        var recentIds = runs
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(8)
            .Select(x => x.Id)
            .ToList();
        var activeIds = runs
            .Where(x => x.Status is ResearchRunStatus.Pending or ResearchRunStatus.Running)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(5)
            .Select(x => x.Id)
            .ToList();

        overview.RecentRuns = await LoadRunSummariesAsync(recentIds, cancellationToken);
        overview.ActiveRunDetails = await LoadRunSummariesAsync(activeIds, cancellationToken);
        overview.RecentCriticalErrors = (await ListErrorsAsync(null, cancellationToken)).Take(8).ToList();

        var sources = await _db.DiscoverySources.AsNoTracking().ToListAsync(cancellationToken);
        overview.TopSources = sources
            .OrderByDescending(x => x.QualifiedProspectsYielded)
            .ThenByDescending(x => x.UniqueCompaniesDiscovered)
            .Take(8)
            .Select(MapSource)
            .ToList();
        overview.UnhealthySources = sources
            .Where(IsUnhealthySource)
            .OrderByDescending(x => x.FailureCount)
            .Take(8)
            .Select(MapSource)
            .ToList();

        return overview;
    }

    public async Task<List<ResearchEvidenceDto>> ListEvidenceForProspectAsync(
        Guid prospectId,
        CancellationToken cancellationToken)
    {
        var items = await _db.ResearchEvidenceItems.AsNoTracking()
            .Where(x => x.ProspectId == prospectId)
            .ToListAsync(cancellationToken);
        items = items.OrderByDescending(x => x.LastVerifiedAtUtc).ToList();

        return items.Select(MapEvidence).ToList();
    }

    public async Task<List<QualificationCriterionResultDto>> ListCriteriaForProspectAsync(
        Guid prospectId,
        CancellationToken cancellationToken)
    {
        var items = await _db.QualificationCriterionResults.AsNoTracking()
            .Where(x => x.ProspectId == prospectId)
            .OrderBy(x => x.CriterionKey)
            .ToListAsync(cancellationToken);

        return items.Select(x => new QualificationCriterionResultDto
        {
            Id = x.Id,
            CriterionKey = x.CriterionKey,
            CriterionLabel = x.CriterionLabel,
            IsMandatory = x.IsMandatory,
            Passed = x.Passed,
            ScoreContribution = x.ScoreContribution,
            Reason = x.Reason,
            EvidenceIds = DeserializeGuidList(x.EvidenceIdsJson),
            EvaluatedAtUtc = x.EvaluatedAtUtc
        }).ToList();
    }

    private async Task<List<ResearchRunSummaryDto>> LoadRunSummariesAsync(
        List<Guid> ids,
        CancellationToken cancellationToken)
    {
        var result = new List<ResearchRunSummaryDto>();
        foreach (var id in ids)
        {
            var summary = await _researchRunService.GetAsync(id, cancellationToken);
            if (summary is not null)
            {
                result.Add(summary);
            }
        }

        return result;
    }

    private static List<ResearchErrorGroupDto> GroupErrors(List<ResearchRunEvent> items)
    {
        var groups = new Dictionary<string, ResearchErrorGroupDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var category = CategorizeError(item);
            var key = $"{category}|{item.Domain}|{item.Message}";
            if (!groups.TryGetValue(key, out var group))
            {
                group = new ResearchErrorGroupDto
                {
                    Category = category,
                    SampleMessage = item.Message ?? item.EventType.ToString(),
                    Count = 0,
                    FirstOccurredAtUtc = item.CreatedAtUtc,
                    LastOccurredAtUtc = item.CreatedAtUtc,
                    ResearchRunId = item.ResearchRunId,
                    Domain = item.Domain,
                    Url = item.Url,
                    Stage = item.Stage.ToString(),
                    SuggestedRemediation = SuggestRemediation(category),
                    RetryPossible = IsRetryPossible(category)
                };
                groups[key] = group;
            }

            group.Count++;
            if (item.CreatedAtUtc < group.FirstOccurredAtUtc)
            {
                group.FirstOccurredAtUtc = item.CreatedAtUtc;
            }

            if (item.CreatedAtUtc > group.LastOccurredAtUtc)
            {
                group.LastOccurredAtUtc = item.CreatedAtUtc;
                group.Url = item.Url ?? group.Url;
                group.ResearchRunId = item.ResearchRunId;
            }
        }

        return groups.Values
            .OrderByDescending(x => x.LastOccurredAtUtc)
            .Take(100)
            .ToList();
    }

    private static string CategorizeError(ResearchRunEvent item)
    {
        var message = item.Message ?? string.Empty;
        if (item.EventType == ResearchRunEventType.RobotsDenied
            || message.Contains("robots.txt", StringComparison.OrdinalIgnoreCase))
        {
            return "robotsDenied";
        }

        if (item.EventType == ResearchRunEventType.SsrfBlocked
            || message.Contains("SSRF", StringComparison.OrdinalIgnoreCase))
        {
            return "ssrfBlocked";
        }

        if (item.EventType == ResearchRunEventType.TimeoutOccurred
            || message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return "timeout";
        }

        if (item.EventType == ResearchRunEventType.RateLimited
            || message.Contains("429", StringComparison.OrdinalIgnoreCase))
        {
            return "rateLimited";
        }

        if (message.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase))
        {
            return "http5xx";
        }

        if (message.Contains("HTTP 4", StringComparison.OrdinalIgnoreCase))
        {
            return "http4xx";
        }

        if (message.Contains("DNS", StringComparison.OrdinalIgnoreCase)
            || message.Contains("request failed", StringComparison.OrdinalIgnoreCase))
        {
            return "networkFailure";
        }

        if (message.Contains("content type", StringComparison.OrdinalIgnoreCase))
        {
            return "unsupportedContentType";
        }

        if (message.Contains("size limit", StringComparison.OrdinalIgnoreCase))
        {
            return "responseSizeLimit";
        }

        return item.EventType.ToString();
    }

    private static string SuggestRemediation(string category)
    {
        return category switch
        {
            "robotsDenied" => "Respect robots.txt. Prefer alternate public pages or skip the domain.",
            "ssrfBlocked" => "URL targets a blocked/private destination. Do not retry without changing the target.",
            "timeout" => "Increase timeout carefully or reduce crawl concurrency for this domain.",
            "rateLimited" => "Back off and lower requests-per-domain for this host.",
            "http5xx" => "Remote server error. Retry later within existing retry budgets.",
            "http4xx" => "Resource is unavailable or forbidden. Verify the URL is public.",
            "networkFailure" => "Check DNS/connectivity for the domain; confirm the host still resolves publicly.",
            "unsupportedContentType" => "Skip non-HTML resources; prefer HTML landing or contact pages.",
            "responseSizeLimit" => "Response exceeded capture limits; try a smaller page or deepen link selection.",
            _ => "Inspect related fetch and event details before retrying."
        };
    }

    private static bool IsRetryPossible(string category)
    {
        return category is "timeout" or "rateLimited" or "http5xx" or "networkFailure";
    }

    private static bool IsUnhealthySource(DiscoverySource source)
    {
        if (!string.IsNullOrWhiteSpace(source.LastError))
        {
            return true;
        }

        if (source.AttemptCount >= 3 && source.FailureCount >= source.SuccessCount)
        {
            return true;
        }

        return source.Status is DiscoverySourceStatus.Disabled
            or DiscoverySourceStatus.EvaluationFailed
            or DiscoverySourceStatus.TemporarilyDeprioritized;
    }

    public static DiscoverySourceSummaryDto MapSource(DiscoverySource source)
    {
        var averageFetchMs = source.PagesAccessible > 0
            ? (double?)source.TotalProcessingMs / source.PagesAccessible
            : null;
        var failureRate = source.AttemptCount > 0
            ? (double?)source.FailureCount / source.AttemptCount
            : null;
        var duplicateRate = source.CandidatesDiscovered > 0
            ? (double?)source.DuplicateHits / source.CandidatesDiscovered
            : null;

        return new DiscoverySourceSummaryDto
        {
            Id = source.Id,
            Name = source.Name,
            BaseUrl = source.BaseUrl,
            NormalizedDomain = source.NormalizedDomain,
            SourceType = source.SourceType.ToString(),
            Status = source.Status.ToString(),
            CountriesJson = source.CountriesJson,
            IndustriesJson = source.IndustriesJson,
            RelevanceScore = source.RelevanceScore,
            FreshnessScore = source.FreshnessScore,
            AttemptCount = source.AttemptCount,
            SuccessCount = source.SuccessCount,
            FailureCount = source.FailureCount,
            CandidatesDiscovered = source.CandidatesDiscovered,
            UniqueCompaniesDiscovered = source.UniqueCompaniesDiscovered,
            RelevantCompaniesDiscovered = source.RelevantCompaniesDiscovered,
            QualifiedProspectsYielded = source.QualifiedProspectsYielded,
            PagesAccessible = source.PagesAccessible,
            PagesParsed = source.PagesParsed,
            LiveVerifiedCompanies = source.LiveVerifiedCompanies,
            PublicContactsExtracted = source.PublicContactsExtracted,
            DuplicateHits = source.DuplicateHits,
            TotalProcessingMs = source.TotalProcessingMs,
            AverageFetchMs = averageFetchMs,
            FailureRate = failureRate,
            DuplicateRate = duplicateRate,
            LastAttemptedAtUtc = source.LastAttemptedAtUtc,
            LastSucceededAtUtc = source.LastSucceededAtUtc,
            LastCheckedAtUtc = source.LastCheckedAtUtc,
            LastError = source.LastError,
            ProvenanceNotes = source.ProvenanceNotes
        };
    }

    private static DiscoveredUrlDto MapDiscoveredUrl(
        DiscoveredUrl x,
        IReadOnlyDictionary<Guid, string>? parentUrls = null)
    {
        string? parentUrl = null;
        if (x.ParentDiscoveredUrlId.HasValue
            && parentUrls is not null
            && parentUrls.TryGetValue(x.ParentDiscoveredUrlId.Value, out var resolved))
        {
            parentUrl = resolved;
        }

        var hasParentEdge = !x.ParentDiscoveredUrlId.HasValue || !string.IsNullOrWhiteSpace(parentUrl);
        return new DiscoveredUrlDto
        {
            Id = x.Id,
            Url = x.Url,
            NormalizedDomain = x.NormalizedDomain,
            SourceType = x.SourceType.ToString(),
            SourceName = x.SourceName,
            DiscoveryReason = x.SourceName,
            ParentDiscoveredUrlId = x.ParentDiscoveredUrlId,
            ParentUrl = parentUrl,
            Status = x.Status.ToString(),
            LiveVerificationStatus = x.LiveVerificationStatus.ToString(),
            DiscoverySourceId = x.DiscoverySourceId,
            Depth = x.Depth,
            RetryCount = x.RetryCount,
            RejectionReasonCode = x.RejectionReasonCode,
            PagePurpose = x.PagePurpose,
            PriorityScore = x.PriorityScore,
            LastError = x.LastError,
            DiscoveredAtUtc = x.DiscoveredAtUtc,
            ProcessedAtUtc = x.ProcessedAtUtc,
            ProvenanceComplete = hasParentEdge && !string.IsNullOrWhiteSpace(x.SourceName)
        };
    }

    private static CrawledPageDto MapCrawledPage(CrawledPage x, bool includeExcerpt)
    {
        return new CrawledPageDto
        {
            Id = x.Id,
            ResearchRunId = x.ResearchRunId,
            DiscoveredUrlId = x.DiscoveredUrlId,
            Url = x.Url,
            NormalizedUrl = x.NormalizedUrl,
            NormalizedDomain = x.NormalizedDomain,
            HttpStatusCode = x.HttpStatusCode,
            ContentType = x.ContentType,
            ContentLengthBytes = x.ContentLengthBytes,
            ContentHash = x.ContentHash,
            CanonicalUrl = x.CanonicalUrl,
            FinalUrl = x.FinalUrl,
            Title = x.Title,
            RobotsAllowed = x.RobotsAllowed,
            BlockedBySsrf = x.BlockedBySsrf,
            WasRedirected = x.WasRedirected,
            RedirectCount = x.RedirectCount,
            DurationMs = x.DurationMs,
            Error = x.Error,
            CrawlDepth = x.CrawlDepth,
            TextExcerpt = includeExcerpt ? x.TextExcerpt : null,
            TextExcerptAvailable = !string.IsNullOrWhiteSpace(x.TextExcerpt),
            CrawledAtUtc = x.CrawledAtUtc
        };
    }

    private static ResearchEvidenceDto MapEvidence(ResearchEvidence item)
    {
        return new ResearchEvidenceDto
        {
            Id = item.Id,
            FactType = item.FactType,
            FactValue = item.FactValue,
            ConfidenceKind = item.ConfidenceKind.ToString(),
            SourceUrl = item.SourceUrl,
            Notes = item.Notes,
            FirstDiscoveredAtUtc = item.FirstDiscoveredAtUtc,
            LastVerifiedAtUtc = item.LastVerifiedAtUtc
        };
    }

    private static List<Guid> DeserializeGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
