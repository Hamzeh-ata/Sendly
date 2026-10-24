using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public class AdaptiveRecoveryTelemetry
{
    public int StageNumber { get; set; }

    public string TriggerSignal { get; set; } = string.Empty;

    public string Strategy { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int CandidatesBefore { get; set; }

    public int CandidatesAfter { get; set; }

    public bool ImprovedYield { get; set; }
}

public class DiscoveryProviderStat
{
    public string ProviderName { get; set; } = string.Empty;

    public bool Attempted { get; set; }

    public bool Succeeded { get; set; }

    public bool ProducedUsefulCandidates { get; set; }

    public int CandidatesReturned { get; set; }

    public int StageNumber { get; set; }

    public string? Error { get; set; }

    public string? ZeroYieldReason { get; set; }
}

public class AutomaticDiscoveryResult
{
    public List<DiscoveredUrlCandidate> Candidates { get; set; } = [];

    public List<DiscoveryProviderStat> ProviderStats { get; set; } = [];

    public List<DiscoveryStageResult> Stages { get; set; } = [];

    public DiscoveryPlan? Plan { get; set; }

    public int DuplicatesRemoved { get; set; }

    public int RejectedUnsafe { get; set; }

    public int RejectedFiltered { get; set; }

    public Dictionary<string, int> UrlRejectionReasons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int UniqueDomains { get; set; }

    public List<AdaptiveRecoveryTelemetry> RecoveryTelemetry { get; set; } = [];

    public DiscoveryStopReason StopReason { get; set; }

    public List<string> RecoveryActions { get; set; } = [];

    public int SourcesReused { get; set; }

    public int SourcesAdded { get; set; }

    public int SourcesSkipped { get; set; }

    public int SourcesUnavailable { get; set; }

    public string? Summary { get; set; }

    public List<SourceEligibilityDecision> SourceEligibilityDecisions { get; set; } = [];

    public int RelevantIndustrySourcesAdmitted { get; set; }

    public int SecondaryRegistrySourcesAdmitted { get; set; }
}

public interface IAutomaticDiscoveryCoordinator
{
    Task<AutomaticDiscoveryResult> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken);
}
