using Microsoft.EntityFrameworkCore;
using Sendly.Application.Services;

namespace Sendly.Infrastructure.Persistence;

public static class SqliteDateTimeOffsetQuery
{
    public static bool IsSqlite(object db)
        => db is DbContext context
           && (context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ?? false);

    public static async Task<List<T>> TakeRecentByStoredUtcAsync<T>(
        object db,
        IQueryable<T> unsortedQuery,
        Func<T, DateTimeOffset> selector,
        FormattableString sqliteSql,
        int take,
        CancellationToken cancellationToken)
        where T : class
    {
        if (db is DbContext context && IsSqlite(context))
        {
            return await context.Set<T>()
                .FromSqlInterpolated(sqliteSql)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        var items = await unsortedQuery.AsNoTracking().ToListAsync(cancellationToken);
        return PriorResearchOrdering.TakeMostRecent(items, selector, take);
    }
}
