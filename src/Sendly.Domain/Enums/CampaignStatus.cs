namespace Sendly.Domain.Enums;

public enum CampaignStatus
{
    Draft = 0,
    Queued = 1,
    Running = 2,
    Completed = 3,
    CompletedWithFailures = 4,
    Cancelled = 5,
    Failed = 6
}
