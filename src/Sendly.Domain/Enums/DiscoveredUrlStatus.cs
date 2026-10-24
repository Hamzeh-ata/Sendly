namespace Sendly.Domain.Enums;

public enum DiscoveredUrlStatus
{
    Pending = 0,
    Queued = 1,
    Crawling = 2,
    Crawled = 3,
    Failed = 4,
    SkippedRobots = 5,
    SkippedDuplicate = 6,
    Cancelled = 7,
    SkippedFiltered = 8,
    SkippedBudget = 9,
    PendingVerification = 10,
    Admitted = 11,
    Rejected = 12,
    Fetched = 13,
    Processed = 14
}
