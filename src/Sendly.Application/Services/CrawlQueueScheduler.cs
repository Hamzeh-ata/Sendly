using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public class CrawlScheduleItem
{
    public DiscoveredUrl Url { get; set; } = null!;

    public UrlPriorityScore Priority { get; set; } = new();

    public UrlPagePurpose PagePurpose { get; set; }

    public string Domain { get; set; } = string.Empty;

    public int HostFetchLimit { get; set; }
}

public static class CrawlQueueScheduler
{
    public const int AdaptiveHostBonusPages = 4;

    public const int MaxAdaptiveHostPages = 40;

    public static int ResolveHostFetchLimit(
        int baseMaxPagesPerDomain,
        bool hasStrongOrganizationEvidence,
        bool hasUsefulPendingPages,
        int investigationBonusPages = 0)
    {
        var baseline = Math.Clamp(baseMaxPagesPerDomain, 1, 50);
        var bonus = 0;
        if (hasStrongOrganizationEvidence && hasUsefulPendingPages)
        {
            bonus += AdaptiveHostBonusPages;
        }

        bonus += Math.Max(0, investigationBonusPages);
        if (bonus <= 0)
        {
            return baseline;
        }

        return Math.Min(MaxAdaptiveHostPages, baseline + bonus);
    }

    public static List<CrawlScheduleItem> RankPending(
        IEnumerable<DiscoveredUrl> pending,
        IReadOnlyList<string> industries,
        IReadOnlyList<string> countries,
        IReadOnlyDictionary<string, int> pagesFetchedPerDomain,
        IReadOnlySet<string>? failedDomains,
        int maxPagesPerDomain,
        int maxPerDomainInBatch,
        IReadOnlyDictionary<string, int>? hostFetchLimits = null,
        IReadOnlySet<string>? strongOrganizationHosts = null)
    {
        var pendingList = pending.ToList();
        var usefulByHost = BuildUsefulPendingHosts(pendingList);
        var scored = new List<CrawlScheduleItem>();
        foreach (var item in pendingList)
        {
            var domain = item.NormalizedDomain
                         ?? UrlCanonicalizer.ExtractDomain(item.Url)
                         ?? "unknown";
            var already = pagesFetchedPerDomain.TryGetValue(domain, out var count) ? count : 0;
            var hostLimit = ResolveConfiguredHostLimit(
                domain,
                maxPagesPerDomain,
                hostFetchLimits,
                strongOrganizationHosts,
                usefulByHost);
            if (already >= hostLimit)
            {
                continue;
            }

            var purpose = UrlAdmissionPolicy.ClassifyPagePurpose(item.Url);
            if (!string.IsNullOrWhiteSpace(item.PagePurpose)
                && Enum.TryParse<UrlPagePurpose>(item.PagePurpose, true, out var stored))
            {
                purpose = stored;
            }

            var geography = UrlAdmissionPolicy.EvaluateGeographyRelevance(item.Url, countries);
            if (!geography.Accepted)
            {
                continue;
            }

            var failed = failedDomains is not null && failedDomains.Contains(domain);
            var priority = UrlPriorityScorer.Score(
                item.Url,
                item.SourceType,
                industries,
                countries,
                parentDomain: domain,
                depth: item.Depth,
                alreadyCrawled: false,
                pagePurpose: purpose,
                domainFetchCount: already,
                domainPreviouslyFailed: failed,
                isFreshSource: item.SourceType is not DiscoverySourceType.PriorResearch);

            if (item.PriorityScore > priority.Total)
            {
                priority.Total = item.PriorityScore;
                priority.Explanation = string.IsNullOrWhiteSpace(priority.Explanation)
                    ? "priority.preserved_persisted_score"
                    : priority.Explanation + "; preserved persisted score";
            }
            else
            {
                item.PriorityScore = priority.Total;
            }

            scored.Add(new CrawlScheduleItem
            {
                Url = item,
                Priority = priority,
                PagePurpose = purpose,
                Domain = domain,
                HostFetchLimit = hostLimit
            });
        }

        var allocated = ApplyFairDomainAllocation(scored, maxPerDomainInBatch);
        return allocated;
    }

    public static List<PendingUrlDiagnostic> ExplainDeferredHighScoreUrls(
        IEnumerable<DiscoveredUrl> pending,
        IReadOnlyList<CrawlScheduleItem> scheduledForBatch,
        int batchSize,
        IReadOnlyDictionary<string, int> pagesFetchedPerDomain,
        int maxPagesPerDomain,
        int minPriority = 70,
        IReadOnlyDictionary<string, int>? hostFetchLimits = null,
        IReadOnlyList<string>? countries = null)
    {
        var selected = new HashSet<string>(
            scheduledForBatch
                .Take(Math.Max(1, batchSize))
                .Select(x => x.Url.NormalizedUrl ?? x.Url.Url),
            StringComparer.OrdinalIgnoreCase);
        var selectedHosts = scheduledForBatch
            .Take(Math.Max(1, batchSize))
            .GroupBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var deferred = new List<PendingUrlDiagnostic>();
        foreach (var item in pending)
        {
            var score = item.PriorityScore;
            if (score < minPriority)
            {
                continue;
            }

            var normalized = item.NormalizedUrl ?? item.Url;
            if (selected.Contains(normalized))
            {
                continue;
            }

            var domain = item.NormalizedDomain
                         ?? UrlCanonicalizer.ExtractDomain(item.Url)
                         ?? "unknown";
            var already = pagesFetchedPerDomain.TryGetValue(domain, out var count) ? count : 0;
            var hostLimit = hostFetchLimits is not null
                            && hostFetchLimits.TryGetValue(domain, out var configured)
                ? configured
                : maxPagesPerDomain;
            var reserved = selectedHosts.TryGetValue(domain, out var hostReserved) ? hostReserved : 0;
            string reason;
            var geography = UrlAdmissionPolicy.EvaluateGeographyRelevance(item.Url, countries ?? []);
            if (!geography.Accepted)
            {
                reason = geography.ReasonCode + " " + geography.Explanation;
            }
            else if (already >= hostLimit)
            {
                reason = $"deferred.host_budget already={already} max={hostLimit}";
            }
            else if (reserved > 0 && score >= minPriority)
            {
                reason =
                    $"deferred.batch_rank_or_host_fairness reservedInBatch={reserved} priority={score} batchSize={batchSize}";
            }
            else
            {
                reason =
                    $"deferred.not_in_batch priority={score} rankBeyondBatch={batchSize} status={item.Status}";
            }

            deferred.Add(new PendingUrlDiagnostic
            {
                Url = item.Url,
                Priority = score,
                PagePurpose = item.PagePurpose ?? string.Empty,
                Host = domain,
                SourceType = item.SourceType.ToString(),
                Reason = reason
            });

            if (deferred.Count >= 20)
            {
                break;
            }
        }

        return deferred
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Url, StringComparer.Ordinal)
            .ToList();
    }

    public static List<CrawlScheduleItem> ApplyFairDomainAllocation(
        List<CrawlScheduleItem> scored,
        int maxPerDomainInBatch)
    {
        var limit = Math.Clamp(maxPerDomainInBatch, 1, 20);
        var ordered = scored
            .OrderByDescending(x => x.Priority.Total)
            .ThenByDescending(x => PurposeBoost(x.PagePurpose))
            .ThenBy(x => x.Url.NormalizedUrl, StringComparer.Ordinal)
            .ThenBy(x => x.Url.DiscoveredAtUtc)
            .ToList();

        var queues = new Dictionary<string, Queue<CrawlScheduleItem>>(StringComparer.OrdinalIgnoreCase);
        var domainOrder = new List<string>();
        foreach (var item in ordered)
        {
            if (!queues.TryGetValue(item.Domain, out var queue))
            {
                queue = new Queue<CrawlScheduleItem>();
                queues[item.Domain] = queue;
                domainOrder.Add(item.Domain);
            }

            queue.Enqueue(item);
        }

        var accepted = new List<CrawlScheduleItem>();
        var perDomain = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var progressed = true;
        while (progressed)
        {
            progressed = false;
            foreach (var domain in domainOrder)
            {
                if (!queues.TryGetValue(domain, out var queue) || queue.Count == 0)
                {
                    continue;
                }

                if (!perDomain.TryGetValue(domain, out var used))
                {
                    used = 0;
                }

                if (used >= limit)
                {
                    continue;
                }

                accepted.Add(queue.Dequeue());
                perDomain[domain] = used + 1;
                progressed = true;
            }
        }

        progressed = true;
        while (progressed)
        {
            progressed = false;
            foreach (var domain in domainOrder)
            {
                if (!queues.TryGetValue(domain, out var queue) || queue.Count == 0)
                {
                    continue;
                }

                accepted.Add(queue.Dequeue());
                progressed = true;
            }
        }

        return accepted;
    }

    public static int ResolveLinkExpansionCap(int maxLinksPerPage, bool parentYieldedRelevantEvidence)
    {
        var configured = Math.Clamp(maxLinksPerPage, 0, 50);
        return parentYieldedRelevantEvidence ? configured : Math.Min(configured, 3);
    }

    public static List<T> ApplyPerDomainCap<T>(
        IEnumerable<T> items,
        Func<T, string?> domainSelector,
        int maxPerDomain)
    {
        var cap = Math.Clamp(maxPerDomain, 1, 200);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<T>();
        foreach (var item in items)
        {
            var domain = domainSelector(item) ?? "unknown";
            var used = counts.TryGetValue(domain, out var current) ? current : 0;
            if (used >= cap)
            {
                continue;
            }

            counts[domain] = used + 1;
            result.Add(item);
        }

        return result;
    }

    public static int CountUsefulPending(IEnumerable<DiscoveredUrl> pending)
    {
        var count = 0;
        foreach (var item in pending)
        {
            var purpose = !string.IsNullOrWhiteSpace(item.PagePurpose)
                          && Enum.TryParse<UrlPagePurpose>(item.PagePurpose, true, out var stored)
                ? stored
                : UrlAdmissionPolicy.ClassifyPagePurpose(item.Url);
            if (UrlAdmissionPolicy.IsUsefulPendingPurpose(purpose)
                || purpose == UrlPagePurpose.Unknown)
            {
                count++;
            }
        }

        return count;
    }

    public static Dictionary<string, int> BuildHostFetchLimits(
        IEnumerable<DiscoveredUrl> pending,
        int baseMaxPagesPerDomain,
        IReadOnlySet<string>? strongOrganizationHosts,
        IReadOnlyDictionary<string, int>? investigationBonuses = null)
    {
        var usefulByHost = BuildUsefulPendingHosts(pending);
        var limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in usefulByHost)
        {
            var strong = strongOrganizationHosts is not null && strongOrganizationHosts.Contains(host.Key);
            var investigationBonus = investigationBonuses is not null
                                     && investigationBonuses.TryGetValue(host.Key, out var bonus)
                ? bonus
                : 0;
            limits[host.Key] = ResolveHostFetchLimit(
                baseMaxPagesPerDomain,
                strong,
                host.Value > 0,
                investigationBonus);
        }

        if (strongOrganizationHosts is null)
        {
            return limits;
        }

        foreach (var host in strongOrganizationHosts)
        {
            if (limits.ContainsKey(host))
            {
                continue;
            }

            limits[host] = ResolveHostFetchLimit(baseMaxPagesPerDomain, true, false);
        }

        return limits;
    }

    private static int ResolveConfiguredHostLimit(
        string domain,
        int maxPagesPerDomain,
        IReadOnlyDictionary<string, int>? hostFetchLimits,
        IReadOnlySet<string>? strongOrganizationHosts,
        IReadOnlyDictionary<string, int> usefulByHost)
    {
        if (hostFetchLimits is not null && hostFetchLimits.TryGetValue(domain, out var configured))
        {
            return configured;
        }

        var strong = strongOrganizationHosts is not null && strongOrganizationHosts.Contains(domain);
        var useful = usefulByHost.TryGetValue(domain, out var usefulCount) && usefulCount > 0;
        return ResolveHostFetchLimit(maxPagesPerDomain, strong, useful);
    }

    private static Dictionary<string, int> BuildUsefulPendingHosts(IEnumerable<DiscoveredUrl> pending)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in pending)
        {
            var domain = item.NormalizedDomain
                         ?? UrlCanonicalizer.ExtractDomain(item.Url)
                         ?? "unknown";
            var purpose = !string.IsNullOrWhiteSpace(item.PagePurpose)
                          && Enum.TryParse<UrlPagePurpose>(item.PagePurpose, true, out var stored)
                ? stored
                : UrlAdmissionPolicy.ClassifyPagePurpose(item.Url);
            if (!UrlAdmissionPolicy.IsUsefulPendingPurpose(purpose))
            {
                continue;
            }

            if (result.TryGetValue(domain, out var count))
            {
                result[domain] = count + 1;
            }
            else
            {
                result[domain] = 1;
            }
        }

        return result;
    }

    private static int PurposeBoost(UrlPagePurpose purpose)
        => purpose switch
        {
            UrlPagePurpose.Contact => 5,
            UrlPagePurpose.Team => 4,
            UrlPagePurpose.About => 3,
            UrlPagePurpose.FranchiseLocation => 3,
            UrlPagePurpose.Location => 2,
            UrlPagePurpose.CompanyProfile => 2,
            _ => 0
        };
}
