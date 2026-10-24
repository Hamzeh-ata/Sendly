using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public static class DiscoveryAdmissionPolicy
{
    public static List<DiscoveredUrlCandidate> ApplyPriorResearchDiversity(
        List<DiscoveredUrlCandidate> candidates,
        UrlDiscoveryContext context)
    {
        var sharePercent = Math.Clamp(context.MaxPriorResearchSharePercent, 0, 100);
        if (sharePercent >= 100 || candidates.Count == 0)
        {
            return candidates;
        }

        var prior = candidates.Where(x => x.SourceType == DiscoverySourceType.PriorResearch).ToList();
        var fresh = candidates.Where(x => x.SourceType != DiscoverySourceType.PriorResearch).ToList();
        if (fresh.Count == 0)
        {
            return prior.Take(context.MaxUrls).ToList();
        }

        var keptFresh = fresh.Take(context.MaxUrls).ToList();
        var remainingSlots = Math.Max(0, context.MaxUrls - keptFresh.Count);
        var maxPriorByShare = (int)Math.Floor(context.MaxUrls * (sharePercent / 100.0));
        if (context.PreferFreshSources)
        {
            maxPriorByShare = Math.Min(maxPriorByShare, (int)Math.Floor(context.MaxUrls * 0.2));
        }

        var relativeCap = (int)Math.Floor((keptFresh.Count + remainingSlots) * (sharePercent / 100.0));
        var maxPrior = Math.Min(remainingSlots, Math.Min(maxPriorByShare, Math.Max(relativeCap, 0)));
        var keptPrior = prior.Take(maxPrior).ToList();
        return PreferFreshOrdering(keptFresh.Concat(keptPrior).ToList());
    }

    public static List<DiscoveredUrlCandidate> PreferFreshOrdering(List<DiscoveredUrlCandidate> candidates)
    {
        return candidates
            .OrderBy(x => x.SourceType == DiscoverySourceType.PriorResearch ? 1 : 0)
            .ThenBy(x => x.Depth)
            .ToList();
    }
}
