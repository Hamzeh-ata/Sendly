namespace Sendly.Application.Services;

public static class PriorResearchOrdering
{
    public static List<T> OrderByUtcDescending<T>(
        IEnumerable<T> items,
        Func<T, DateTimeOffset> selector)
    {
        return items
            .OrderByDescending(x => selector(x).UtcTicks)
            .ToList();
    }

    public static List<T> TakeMostRecent<T>(
        IEnumerable<T> items,
        Func<T, DateTimeOffset> selector,
        int take)
    {
        var limit = Math.Max(0, take);
        return OrderByUtcDescending(items, selector).Take(limit).ToList();
    }
}
