namespace Sendly.Application.Services;

public enum ResearchBudgetKind
{
    SearchRequest = 0,
    VerificationFetch = 1,
    ContactFetch = 2,
    LinkExpansion = 3,
    OllamaCall = 4
}

public class ResearchBudgetLimits
{
    public int SearchRequests { get; set; }
    public int VerificationFetches { get; set; }
    public int ContactFetches { get; set; }
    public int LinkExpansionFetches { get; set; }
    public int OllamaCalls { get; set; }
    public int MaxDurationMinutes { get; set; } = 30;
}

public class ResearchBudgetSnapshot
{
    public Dictionary<string, int> Used { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Limits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Remaining { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool DurationExceeded { get; set; }
}

public class ResearchBudgetManager
{
    private readonly ResearchBudgetLimits _limits;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly Dictionary<ResearchBudgetKind, int> _used = new();

    public ResearchBudgetManager(ResearchBudgetLimits limits, DateTimeOffset startedAtUtc, ResearchBudgetUsage? initialUsage = null)
    {
        _limits = limits;
        _startedAtUtc = startedAtUtc;
        if (initialUsage is null)
        {
            return;
        }

        _used[ResearchBudgetKind.SearchRequest] = initialUsage.SearchRequests;
        _used[ResearchBudgetKind.VerificationFetch] = initialUsage.VerificationFetches;
        _used[ResearchBudgetKind.ContactFetch] = initialUsage.ContactFetches;
        _used[ResearchBudgetKind.LinkExpansion] = initialUsage.LinkExpansionFetches;
        _used[ResearchBudgetKind.OllamaCall] = initialUsage.OllamaCalls;
    }

    public bool TryReserve(ResearchBudgetKind kind, int amount = 1)
    {
        if (amount <= 0 || IsDurationExceeded())
        {
            return false;
        }

        var limit = LimitFor(kind);
        if (limit <= 0)
        {
            return false;
        }

        var used = UsedFor(kind);
        if (used + amount > limit)
        {
            return false;
        }

        _used[kind] = used + amount;
        return true;
    }

    public bool CanReserve(ResearchBudgetKind kind, int amount = 1)
    {
        if (amount <= 0 || IsDurationExceeded())
        {
            return false;
        }

        var limit = LimitFor(kind);
        return limit > 0 && UsedFor(kind) + amount <= limit;
    }

    public int UsedFor(ResearchBudgetKind kind)
        => _used.TryGetValue(kind, out var value) ? value : 0;

    public int RemainingFor(ResearchBudgetKind kind)
        => Math.Max(0, LimitFor(kind) - UsedFor(kind));

    public bool IsDurationExceeded()
    {
        var minutes = Math.Clamp(_limits.MaxDurationMinutes, 1, 240);
        return DateTimeOffset.UtcNow - _startedAtUtc > TimeSpan.FromMinutes(minutes);
    }

    public ResearchBudgetSnapshot Snapshot()
    {
        var snapshot = new ResearchBudgetSnapshot { DurationExceeded = IsDurationExceeded() };
        foreach (var kind in Enum.GetValues<ResearchBudgetKind>())
        {
            var name = kind.ToString();
            snapshot.Used[name] = UsedFor(kind);
            snapshot.Limits[name] = LimitFor(kind);
            snapshot.Remaining[name] = RemainingFor(kind);
        }

        return snapshot;
    }

    private int LimitFor(ResearchBudgetKind kind)
    {
        return kind switch
        {
            ResearchBudgetKind.SearchRequest => _limits.SearchRequests,
            ResearchBudgetKind.VerificationFetch => _limits.VerificationFetches,
            ResearchBudgetKind.ContactFetch => _limits.ContactFetches,
            ResearchBudgetKind.LinkExpansion => _limits.LinkExpansionFetches,
            ResearchBudgetKind.OllamaCall => _limits.OllamaCalls,
            _ => 0
        };
    }
}

public class ResearchBudgetUsage
{
    public int SearchRequests { get; set; }
    public int VerificationFetches { get; set; }
    public int ContactFetches { get; set; }
    public int LinkExpansionFetches { get; set; }
    public int OllamaCalls { get; set; }
}
