using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public class DiscoverySourceCapabilities
{
    public string Name { get; set; } = string.Empty;

    public DiscoverySourceType SourceType { get; set; }

    public bool SupportsIndustryFilter { get; set; }

    public bool SupportsCountryFilter { get; set; }

    public bool RequiresPaidApi { get; set; }

    public bool RequiresHumanVerification { get; set; }

    public string AccessNotes { get; set; } = string.Empty;

    public List<string> SupportedRegions { get; set; } = [];
}

public class DiscoverySourceHealthReport
{
    public string Name { get; set; } = string.Empty;

    public SearchProviderHealthState Health { get; set; } = SearchProviderHealthState.NoResults;

    public DateTimeOffset? LastSuccessAtUtc { get; set; }

    public string? LastFailureSummary { get; set; }

    public bool IsTemporarilyDisabled { get; set; }

    public int UsefulResultYield { get; set; }

    public List<string> AccessRestrictions { get; set; } = [];
}

public interface IDiscoverySourceAdapter
{
    DiscoverySourceCapabilities Capabilities { get; }

    DiscoverySourceHealthReport GetHealth(DateTimeOffset nowUtc);
}
