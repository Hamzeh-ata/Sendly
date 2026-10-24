using System.Text.Json;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;

namespace Sendly.Application.Services;

public static class ResearchRunMapper
{
    public static ResearchRunSummaryDto MapSummary(ResearchRun run, string? projectName)
    {
        return new ResearchRunSummaryDto
        {
            Id = run.Id,
            ProjectId = run.ProjectId,
            ProjectName = projectName,
            ResearchQueryId = run.ResearchQueryId,
            ResearchQueryVersionId = run.ResearchQueryVersionId,
            UrlsDiscovered = run.UrlsDiscovered,
            PagesCrawled = run.PagesCrawled,
            CrawlErrors = run.CrawlErrors,
            Status = run.Status,
            Mode = run.Mode,
            MapRegionsCompleted = run.MapRegionsCompleted,
            MapRegionsTotal = run.MapRegionsTotal,
            MapPlacesDiscovered = run.MapPlacesDiscovered,
            RequestedProspectCount = run.RequestedProspectCount,
            IncludeEmail = run.IncludeEmail,
            IncludeWhatsApp = run.IncludeWhatsApp,
            MinQualificationScore = run.MinQualificationScore,
            ProgressPercent = run.ProgressPercent,
            CompaniesFound = run.CompaniesFound,
            ContactsFound = run.ContactsFound,
            ValidEmails = run.ValidEmails,
            ValidWhatsApp = run.ValidWhatsApp,
            DuplicatesFound = run.DuplicatesFound,
            RejectedCount = run.RejectedCount,
            QualifiedCount = run.QualifiedCount,
            SearchQueriesExecuted = run.SearchQueriesExecuted,
            SearchResultsReceived = run.SearchResultsReceived,
            CandidatesDiscovered = run.CandidatesDiscovered,
            CandidatesAfterDeduplication = run.CandidatesAfterDeduplication,
            CandidatesValidated = run.CandidatesValidated,
            ProspectsSaved = run.ProspectsSaved,
            ProspectsSkippedAsDuplicates = run.ProspectsSkippedAsDuplicates,
            ResearchLimitsReached = run.ResearchLimitsReached,
            ResearchLimitsReachedReason = run.ResearchLimitsReachedReason,
            RejectionReasons = DeserializeRejectionReasons(run.RejectionReasonsJson),
            SearchProviderDiagnostics = DeserializeDiagnostics(run.SearchProviderDiagnosticsJson),
            Error = run.Error,
            Warning = run.Warning,
            CreatedAtUtc = run.CreatedAtUtc,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc
        };
    }

    public static ResearchRunDetailDto MapDetail(
        ResearchRun run,
        string? projectName,
        IEnumerable<Prospect>? prospects = null)
    {
        var summary = MapSummary(run, projectName);
        return new ResearchRunDetailDto
        {
            Id = summary.Id,
            ProjectId = summary.ProjectId,
            ProjectName = summary.ProjectName,
            ResearchQueryId = summary.ResearchQueryId,
            ResearchQueryVersionId = summary.ResearchQueryVersionId,
            ResearchQueryName = summary.ResearchQueryName,
            UrlsDiscovered = summary.UrlsDiscovered,
            PagesCrawled = summary.PagesCrawled,
            CrawlErrors = summary.CrawlErrors,
            Status = summary.Status,
            Mode = summary.Mode,
            MapRegionsCompleted = summary.MapRegionsCompleted,
            MapRegionsTotal = summary.MapRegionsTotal,
            MapPlacesDiscovered = summary.MapPlacesDiscovered,
            RequestedProspectCount = summary.RequestedProspectCount,
            IncludeEmail = summary.IncludeEmail,
            IncludeWhatsApp = summary.IncludeWhatsApp,
            MinQualificationScore = summary.MinQualificationScore,
            ProgressPercent = summary.ProgressPercent,
            CompaniesFound = summary.CompaniesFound,
            ContactsFound = summary.ContactsFound,
            ValidEmails = summary.ValidEmails,
            ValidWhatsApp = summary.ValidWhatsApp,
            DuplicatesFound = summary.DuplicatesFound,
            RejectedCount = summary.RejectedCount,
            QualifiedCount = summary.QualifiedCount,
            SearchQueriesExecuted = summary.SearchQueriesExecuted,
            SearchResultsReceived = summary.SearchResultsReceived,
            CandidatesDiscovered = summary.CandidatesDiscovered,
            CandidatesAfterDeduplication = summary.CandidatesAfterDeduplication,
            CandidatesValidated = summary.CandidatesValidated,
            ProspectsSaved = summary.ProspectsSaved,
            ProspectsSkippedAsDuplicates = summary.ProspectsSkippedAsDuplicates,
            ResearchLimitsReached = summary.ResearchLimitsReached,
            ResearchLimitsReachedReason = summary.ResearchLimitsReachedReason,
            RejectionReasons = summary.RejectionReasons,
            SearchProviderDiagnostics = summary.SearchProviderDiagnostics,
            Error = summary.Error,
            Warning = summary.Warning,
            CreatedAtUtc = summary.CreatedAtUtc,
            StartedAtUtc = summary.StartedAtUtc,
            CompletedAtUtc = summary.CompletedAtUtc,
            Prospects = MapProspectItems(prospects)
        };
    }

    public static List<ResearchRunProspectItemDto> MapProspectItems(IEnumerable<Prospect>? prospects)
    {
        if (prospects is null)
        {
            return [];
        }

        return prospects
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new ResearchRunProspectItemDto
            {
                Id = x.Id,
                CompanyName = x.CompanyName,
                Website = x.Website,
                City = x.City,
                Country = x.Country,
                Email = x.Email,
                WhatsApp = x.WhatsApp,
                SourceUrl = x.SourceUrl,
                QualificationScore = x.QualificationScore,
                Status = x.Status,
                IsQualified = x.IsQualified,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToList();
    }

    private static Dictionary<string, int> DeserializeRejectionReasons(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, int>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json)
                   ?? new Dictionary<string, int>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>();
        }
    }

    private static ResearchSearchProviderDiagnosticsDto? DeserializeDiagnostics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new ResearchSearchProviderDiagnosticsDto
            {
                Provider = root.TryGetProperty("provider", out var provider)
                    ? provider.GetString() ?? string.Empty
                    : string.Empty,
                OllamaChecked = ReadBool(root, "ollamaChecked"),
                OllamaReachable = ReadBool(root, "ollamaReachable"),
                OllamaModelAvailable = ReadBool(root, "ollamaModelAvailable"),
                OllamaModel = root.TryGetProperty("ollamaModel", out var ollamaModel)
                    ? ollamaModel.GetString()
                    : null,
                OllamaQueriesGenerated = ReadInt(root, "ollamaQueriesGenerated"),
                QueriesExecuted = ReadInt(root, "queriesExecuted"),
                RequestsSuccessful = ReadInt(root, "requestsSuccessful"),
                RequestsFailed = ReadInt(root, "requestsFailed"),
                ResultsReceived = ReadInt(root, "resultsReceived"),
                Http401 = ReadInt(root, "http401"),
                Http429 = ReadInt(root, "http429"),
                Http5xx = ReadInt(root, "http5xx"),
                Timeouts = ReadInt(root, "timeouts"),
                AutomaticDiscoverySummary = root.TryGetProperty("automaticDiscoverySummary", out var summary)
                    ? summary.GetString()
                    : null,
                AutomaticDiscoveryDuplicatesRemoved = ReadInt(root, "automaticDiscoveryDuplicatesRemoved"),
                AutomaticDiscoveryRejectedUnsafe = ReadInt(root, "automaticDiscoveryRejectedUnsafe"),
                DiscoveryPlanSummary = root.TryGetProperty("discoveryPlanSummary", out var plan)
                    ? plan.GetString()
                    : null,
                UniqueDomainsDiscovered = ReadInt(root, "uniqueDomainsDiscovered"),
                RecoveryAttempts = ReadInt(root, "recoveryAttempts"),
                DiscoveryStopReason = root.TryGetProperty("discoveryStopReason", out var stop)
                    ? stop.GetString()
                    : null,
                RecoveryStrategies = ReadStringList(root, "recoveryStrategies"),
                SourcesReused = ReadInt(root, "sourcesReused"),
                SourcesAdded = ReadInt(root, "sourcesAdded"),
                SourcesSkipped = ReadInt(root, "sourcesSkipped"),
                SourcesUnavailable = ReadInt(root, "sourcesUnavailable"),
                UrlsAccessible = ReadInt(root, "urlsAccessible"),
                UrlsParsed = ReadInt(root, "urlsParsed"),
                UrlsUnavailable = ReadInt(root, "urlsUnavailable"),
                UrlsRobotsBlocked = ReadInt(root, "urlsRobotsBlocked"),
                CompaniesIdentified = ReadInt(root, "companiesIdentified"),
                PublicContactsExtracted = ReadInt(root, "publicContactsExtracted"),
                LiveVerifiedCompanies = ReadInt(root, "liveVerifiedCompanies"),
                ProspectsQualified = ReadInt(root, "prospectsQualified"),
                UrlsRejectedFiltered = ReadInt(root, "urlsRejectedFiltered"),
                UrlsAdmitted = ReadInt(root, "urlsAdmitted"),
                UrlsReusedFromCache = ReadInt(root, "urlsReusedFromCache"),
                UrlsSkippedKnownProspect = ReadInt(root, "urlsSkippedKnownProspect"),
                UsefulPendingUrls = ReadInt(root, "usefulPendingUrls"),
                UsefulUrlsLeftPendingAtStop = ReadInt(root, "usefulUrlsLeftPendingAtStop"),
                NewlyDiscoveredProspects = ReadInt(root, "newlyDiscoveredProspects"),
                PreviouslyKnownProspects = ReadInt(root, "previouslyKnownProspects"),
                PreviouslyContactedProspects = ReadInt(root, "previouslyContactedProspects"),
                NewlyOutreachEligibleProspects = ReadInt(root, "newlyOutreachEligibleProspects"),
                IdentityVerifiedBusinesses = ReadInt(root, "identityVerifiedBusinesses"),
                EmailReadyProspects = ReadInt(root, "emailReadyProspects"),
                ContactableProspects = ReadInt(root, "contactableProspects"),
                PagesFetched = ReadInt(root, "pagesFetched"),
                PagesQueued = ReadInt(root, "pagesQueued"),
                PagesSkipped = ReadInt(root, "pagesSkipped"),
                FetchBudgetConsumed = ReadInt(root, "fetchBudgetConsumed"),
                FetchBudgetLimit = ReadInt(root, "fetchBudgetLimit"),
                RequestedFetchBudget = ReadInt(root, "requestedFetchBudget"),
                ConfiguredBatchSize = ReadInt(root, "configuredBatchSize"),
                BatchesStarted = ReadInt(root, "batchesStarted"),
                BatchesCompleted = ReadInt(root, "batchesCompleted"),
                FetchesAttempted = ReadInt(root, "fetchesAttempted"),
                FetchesSucceeded = ReadInt(root, "fetchesSucceeded"),
                FetchesFailed = ReadInt(root, "fetchesFailed"),
                FetchesSkipped = ReadInt(root, "fetchesSkipped"),
                BudgetReserved = ReadInt(root, "budgetReserved"),
                BudgetRemaining = ReadInt(root, "budgetRemaining"),
                EligiblePendingUrls = ReadInt(root, "eligiblePendingUrls"),
                DeferredUrls = ReadInt(root, "deferredUrls"),
                MoreEligibleWorkRemained = ReadBool(root, "moreEligibleWorkRemained"),
                LastBudgetStopReason = root.TryGetProperty("lastBudgetStopReason", out var budgetStop)
                    ? budgetStop.GetString()
                    : null,
                SearchLimitsExhausted = ReadBool(root, "searchLimitsExhausted"),
                SearchLimitsExhaustedReason = root.TryGetProperty("searchLimitsExhaustedReason", out var searchLimit)
                    ? searchLimit.GetString()
                    : null,
                LimitsExhausted = ReadBool(root, "limitsExhausted"),
                LimitsExhaustedReason = root.TryGetProperty("limitsExhaustedReason", out var limitsReason)
                    ? limitsReason.GetString()
                    : null,
                StageDurationsMs = ReadLongDictionary(root, "stageDurationsMs"),
                UrlRejectionReasons = ReadIntDictionary(root, "urlRejectionReasons"),
                FetchFailureReasons = ReadIntDictionary(root, "fetchFailureReasons"),
                DiscoveryStages = ReadDiscoveryStages(root),
                DiscoveryProviders = ReadDiscoveryProviders(root),
                TopPendingUrls = ReadTopPendingUrls(root),
                DeferredHighScoreUrls = ReadPendingUrlDiagnostics(root, "deferredHighScoreUrls"),
                CurrentInvestigationActivity = root.TryGetProperty("currentInvestigationActivity", out var activity)
                    ? activity.GetString()
                    : null,
                UnderCoveredIndustries = ReadStringList(root, "underCoveredIndustries"),
                NextBestInvestigationStrategy = root.TryGetProperty("nextBestInvestigationStrategy", out var next)
                    ? next.GetString()
                    : null,
                SearchProviderHealth = root.TryGetProperty("searchProviderHealth", out var health)
                    ? health.GetString()
                    : null,
                SearchProviderHealthSummary = root.TryGetProperty("searchProviderHealthSummary", out var healthSummary)
                    ? healthSummary.GetString()
                    : null,
                LastHeartbeatAtUtc = root.TryGetProperty("lastHeartbeatAtUtc", out var heartbeat)
                    && heartbeat.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(heartbeat.GetString(), out var heartbeatValue)
                    ? heartbeatValue
                    : null
            };
        }
        catch (JsonException)
        {
            return TryRecoverTruncatedDiagnostics(json);
        }
    }

    private static List<DiscoveryStageDto> ReadDiscoveryStages(JsonElement root)
    {
        if (!root.TryGetProperty("discoveryStages", out var stages)
            || stages.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<DiscoveryStageDto>();
        foreach (var item in stages.EnumerateArray())
        {
            var providers = new List<string>();
            if (item.TryGetProperty("providersAttempted", out var attempted)
                && attempted.ValueKind == JsonValueKind.Array)
            {
                foreach (var provider in attempted.EnumerateArray())
                {
                    var value = provider.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        providers.Add(value);
                    }
                }
            }

            result.Add(new DiscoveryStageDto
            {
                StageNumber = item.TryGetProperty("stageNumber", out var number)
                    && number.ValueKind == JsonValueKind.Number
                        ? number.GetInt32()
                        : 0,
                StageName = item.TryGetProperty("stageName", out var name)
                    ? name.GetString() ?? string.Empty
                    : string.Empty,
                ProvidersAttempted = providers,
                CandidatesAdded = item.TryGetProperty("candidatesAdded", out var added)
                    && added.ValueKind == JsonValueKind.Number
                        ? added.GetInt32()
                        : 0,
                RecoveryTriggered = item.TryGetProperty("recoveryTriggered", out var recovery)
                    && recovery.ValueKind == JsonValueKind.True,
                Notes = item.TryGetProperty("notes", out var notes) ? notes.GetString() : null
            });
        }

        return result;
    }

    private static List<DiscoveryProviderStatDto> ReadDiscoveryProviders(JsonElement root)
    {
        if (!root.TryGetProperty("discoveryProviders", out var providers)
            || providers.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<DiscoveryProviderStatDto>();
        foreach (var item in providers.EnumerateArray())
        {
            result.Add(new DiscoveryProviderStatDto
            {
                ProviderName = item.TryGetProperty("providerName", out var name)
                    ? name.GetString() ?? string.Empty
                    : string.Empty,
                Attempted = item.TryGetProperty("attempted", out var attempted) && attempted.GetBoolean(),
                Succeeded = item.TryGetProperty("succeeded", out var succeeded) && succeeded.GetBoolean(),
                ProducedUsefulCandidates = item.TryGetProperty("producedUsefulCandidates", out var useful)
                    && useful.ValueKind == JsonValueKind.True,
                CandidatesReturned = item.TryGetProperty("candidatesReturned", out var count)
                    && count.ValueKind == JsonValueKind.Number
                        ? count.GetInt32()
                        : 0,
                StageNumber = item.TryGetProperty("stageNumber", out var stage)
                    && stage.ValueKind == JsonValueKind.Number
                        ? stage.GetInt32()
                        : 0,
                Error = item.TryGetProperty("error", out var error) ? error.GetString() : null,
                ZeroYieldReason = item.TryGetProperty("zeroYieldReason", out var zero)
                    ? zero.GetString()
                    : null
            });
        }

        return result;
    }

    private static Dictionary<string, int> ReadIntDictionary(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in prop.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.Number)
            {
                result[item.Name] = item.Value.GetInt32();
            }
        }

        return result;
    }

    private static Dictionary<string, long> ReadLongDictionary(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in prop.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.Number)
            {
                result[item.Name] = item.Value.GetInt64();
            }
        }

        return result;
    }

    private static List<string> ReadStringList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static int ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
        {
            return 0;
        }

        return prop.ValueKind == JsonValueKind.Number ? prop.GetInt32() : 0;
    }

    private static bool ReadBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
        {
            return false;
        }

        return prop.ValueKind == JsonValueKind.True;
    }

    private static List<PendingUrlDiagnosticDto> ReadTopPendingUrls(JsonElement root)
        => ReadPendingUrlDiagnostics(root, "topPendingUrls");

    private static List<PendingUrlDiagnosticDto> ReadPendingUrlDiagnostics(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<PendingUrlDiagnosticDto>();
        foreach (var item in items.EnumerateArray())
        {
            result.Add(new PendingUrlDiagnosticDto
            {
                Url = item.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                Priority = item.TryGetProperty("priority", out var priority)
                    && priority.ValueKind == JsonValueKind.Number
                        ? priority.GetInt32()
                        : 0,
                PagePurpose = item.TryGetProperty("pagePurpose", out var purpose)
                    ? purpose.GetString() ?? string.Empty
                    : string.Empty,
                Host = item.TryGetProperty("host", out var host) ? host.GetString() ?? string.Empty : string.Empty,
                SourceType = item.TryGetProperty("sourceType", out var source)
                    ? source.GetString() ?? string.Empty
                    : string.Empty,
                Reason = item.TryGetProperty("reason", out var reason)
                    ? reason.GetString() ?? string.Empty
                    : string.Empty
            });
        }

        return result;
    }

    private static ResearchSearchProviderDiagnosticsDto TryRecoverTruncatedDiagnostics(string json)
    {
        var activity = ExtractJsonString(json, "currentInvestigationActivity");
        var health = ExtractJsonString(json, "searchProviderHealth");
        return new ResearchSearchProviderDiagnosticsDto
        {
            CurrentInvestigationActivity = activity,
            SearchProviderHealth = health,
            SearchProviderHealthSummary = ExtractJsonString(json, "searchProviderHealthSummary")
        };
    }

    private static string? ExtractJsonString(string json, string propertyName)
    {
        var token = $"\"{propertyName}\":";
        var index = json.IndexOf(token, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = json.IndexOf('"', index + token.Length);
        if (start < 0)
        {
            return null;
        }

        var end = json.IndexOf('"', start + 1);
        if (end < 0)
        {
            return json[(start + 1)..];
        }

        return json[(start + 1)..end];
    }
}
