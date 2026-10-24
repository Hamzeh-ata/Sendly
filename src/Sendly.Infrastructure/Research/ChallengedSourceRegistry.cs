using System.Collections.Concurrent;
using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;

namespace Sendly.Infrastructure.Research;

public class ChallengedSourceRegistry : IChallengedSourceRegistry
{
    private static readonly TimeSpan DefaultDisable = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RateLimitDisable = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, ChallengedSourceSnapshot> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsTemporarilyDisabled(string sourceKey, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return false;
        }

        if (!_entries.TryGetValue(sourceKey.Trim(), out var entry))
        {
            return false;
        }

        return entry.DisabledUntilUtc > nowUtc;
    }

    public void RecordChallenge(
        string sourceKey,
        HttpAccessResponseKind kind,
        string summary,
        DateTimeOffset nowUtc,
        int? httpStatusCode = null,
        TimeSpan? disableFor = null)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return;
        }

        var key = sourceKey.Trim();
        var duration = disableFor ?? ResolveDisableDuration(kind);
        _entries.AddOrUpdate(
            key,
            _ => new ChallengedSourceSnapshot
            {
                SourceKey = key,
                Kind = kind,
                Summary = summary,
                ChallengedAtUtc = nowUtc,
                DisabledUntilUtc = nowUtc.Add(duration),
                ChallengeCount = 1,
                HttpStatusCode = httpStatusCode
            },
            (_, existing) =>
            {
                existing.Kind = kind;
                existing.Summary = summary;
                existing.ChallengedAtUtc = nowUtc;
                existing.DisabledUntilUtc = nowUtc.Add(duration);
                existing.ChallengeCount++;
                existing.HttpStatusCode = httpStatusCode ?? existing.HttpStatusCode;
                return existing;
            });
    }

    public void RecordSuccess(string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return;
        }

        _entries.TryRemove(sourceKey.Trim(), out _);
    }

    public IReadOnlyList<ChallengedSourceSnapshot> ListActive(DateTimeOffset nowUtc)
    {
        var result = new List<ChallengedSourceSnapshot>();
        foreach (var entry in _entries.Values)
        {
            if (entry.DisabledUntilUtc > nowUtc)
            {
                result.Add(Clone(entry));
            }
        }

        return result;
    }

    public IReadOnlyList<string> ListDisabledKeys(DateTimeOffset nowUtc)
    {
        var keys = new List<string>();
        foreach (var entry in ListActive(nowUtc))
        {
            keys.Add(entry.SourceKey);
        }

        return keys;
    }

    private static TimeSpan ResolveDisableDuration(HttpAccessResponseKind kind)
        => kind switch
        {
            HttpAccessResponseKind.RateLimited => RateLimitDisable,
            HttpAccessResponseKind.NetworkTimeout => TimeSpan.FromMinutes(5),
            HttpAccessResponseKind.TransientError => TimeSpan.FromMinutes(5),
            _ => DefaultDisable
        };

    private static ChallengedSourceSnapshot Clone(ChallengedSourceSnapshot entry)
        => new()
        {
            SourceKey = entry.SourceKey,
            Kind = entry.Kind,
            Summary = entry.Summary,
            ChallengedAtUtc = entry.ChallengedAtUtc,
            DisabledUntilUtc = entry.DisabledUntilUtc,
            ChallengeCount = entry.ChallengeCount,
            HttpStatusCode = entry.HttpStatusCode
        };
}
