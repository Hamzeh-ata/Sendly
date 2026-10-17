using System.Text.Json;

namespace Sendly.Application.Abstractions;

public class ResearchDiscoveryState
{
    public HashSet<string> SeenHosts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> SeenCandidateKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> CompletedSourceNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> GeneratedQueries { get; set; } = [];

    public int NextQueryIndex { get; set; }

    public int QueryExpansionRound { get; set; }

    public int SearchQueriesExecuted { get; set; }

    public int SearchResultsReceived { get; set; }

    public int SearchRequestsSuccessful { get; set; }

    public int SearchRequestsFailed { get; set; }

    public int Http401Count { get; set; }

    public int Http429Count { get; set; }

    public int Http5xxCount { get; set; }

    public int TimeoutCount { get; set; }

    public int CandidatesDiscovered { get; set; }

    public int CandidatesAfterDeduplication { get; set; }

    public int CandidatesValidated { get; set; }

    public int ProspectsSaved { get; set; }

    public int ProspectsSkippedAsDuplicates { get; set; }

    public Dictionary<string, int> RejectionReasons { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool LimitsExhausted { get; set; }

    public string? LimitsExhaustedReason { get; set; }

    public string? ActiveProviderName { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public void RecordRejection(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var key = reason.Trim();
        if (RejectionReasons.TryGetValue(key, out var count))
        {
            RejectionReasons[key] = count + 1;
            return;
        }

        RejectionReasons[key] = 1;
    }

    public string SerializeRejectionReasons()
    {
        return JsonSerializer.Serialize(RejectionReasons);
    }

    public string SerializeProviderDiagnostics()
    {
        return JsonSerializer.Serialize(new
        {
            provider = ActiveProviderName ?? "Unknown",
            queriesExecuted = SearchQueriesExecuted,
            requestsSuccessful = SearchRequestsSuccessful,
            requestsFailed = SearchRequestsFailed,
            resultsReceived = SearchResultsReceived,
            http401 = Http401Count,
            http429 = Http429Count,
            http5xx = Http5xxCount,
            timeouts = TimeoutCount
        });
    }
}
