namespace Sendly.Domain.Enums;

public enum SearchProviderHealthState
{
    Healthy = 0,
    Degraded = 1,
    Unavailable = 2,
    InvalidResponse = 3,
    NoResults = 4,
    ResultsFiltered = 5
}
