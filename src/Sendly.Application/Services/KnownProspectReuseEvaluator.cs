using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public class KnownProspectReuseDecision
{
    public bool SkipFetch { get; set; }

    public bool ReuseEvidence { get; set; }

    public string ReasonCode { get; set; } = "reuse.none";

    public string Explanation { get; set; } = string.Empty;

    public Guid? ProspectId { get; set; }

    public ProspectStatus? ProspectStatus { get; set; }
}

public sealed class KnownProspectIndex
{
    public Dictionary<string, List<ProspectSiteEntry>> ByDomain { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProspectSiteEntry
{
    public required Prospect Prospect { get; init; }

    public required string CanonicalWebsite { get; init; }

    public required string PathPrefix { get; init; }

    public bool IsDomainRoot { get; init; }
}

public static class KnownProspectReuseEvaluator
{
    public static KnownProspectReuseDecision Evaluate(
        string url,
        KnownProspectIndex index,
        DateTimeOffset nowUtc,
        int evidenceReuseMaxAgeDays)
    {
        var decision = new KnownProspectReuseDecision();
        var prospect = FindMatchingProspect(url, index);
        if (prospect is null)
        {
            decision.ReasonCode = "reuse.unknown";
            decision.Explanation = "No previously known prospect for this site.";
            return decision;
        }

        return Decide(prospect, nowUtc, evidenceReuseMaxAgeDays);
    }

    public static KnownProspectReuseDecision Evaluate(
        string url,
        IReadOnlyDictionary<string, Prospect> prospectsByDomain,
        DateTimeOffset nowUtc,
        int evidenceReuseMaxAgeDays)
    {
        var index = new KnownProspectIndex();
        foreach (var pair in prospectsByDomain)
        {
            AddProspect(index, pair.Value);
        }

        return Evaluate(url, index, nowUtc, evidenceReuseMaxAgeDays);
    }

    public static KnownProspectIndex IndexProspects(IEnumerable<Prospect> prospects)
    {
        var index = new KnownProspectIndex();
        foreach (var prospect in prospects)
        {
            AddProspect(index, prospect);
        }

        return index;
    }

    public static Dictionary<string, Prospect> IndexByDomain(IEnumerable<Prospect> prospects)
    {
        var map = new Dictionary<string, Prospect>(StringComparer.OrdinalIgnoreCase);
        foreach (var prospect in prospects)
        {
            var domain = UrlCanonicalizer.ExtractDomain(prospect.Website)
                         ?? UrlCanonicalizer.ExtractDomain(prospect.SourceUrl);
            if (string.IsNullOrWhiteSpace(domain))
            {
                continue;
            }

            if (!map.TryGetValue(domain, out var existing)
                || prospect.UpdatedAtUtc > existing.UpdatedAtUtc)
            {
                map[domain] = prospect;
            }
        }

        return map;
    }

    private static void AddProspect(KnownProspectIndex index, Prospect prospect)
    {
        var website = prospect.Website ?? prospect.SourceUrl;
        var domain = UrlCanonicalizer.ExtractDomain(website);
        var canonical = UrlCanonicalizer.Canonicalize(website);
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(canonical))
        {
            return;
        }

        if (!index.ByDomain.TryGetValue(domain, out var entries))
        {
            entries = [];
            index.ByDomain[domain] = entries;
        }

        var path = ExtractPath(canonical);
        entries.Add(new ProspectSiteEntry
        {
            Prospect = prospect,
            CanonicalWebsite = canonical,
            PathPrefix = path,
            IsDomainRoot = path is "/" or ""
        });
    }

    private static Prospect? FindMatchingProspect(string url, KnownProspectIndex index)
    {
        var domain = UrlCanonicalizer.ExtractDomain(url);
        var canonical = UrlCanonicalizer.Canonicalize(url);
        if (string.IsNullOrWhiteSpace(domain)
            || string.IsNullOrWhiteSpace(canonical)
            || !index.ByDomain.TryGetValue(domain, out var entries)
            || entries.Count == 0)
        {
            return null;
        }

        var urlPath = ExtractPath(canonical);
        Prospect? best = null;
        var bestScore = -1;
        foreach (var entry in entries)
        {
            var score = ScoreSiteMatch(canonical, urlPath, entry);
            if (score > bestScore)
            {
                bestScore = score;
                best = entry.Prospect;
            }
        }

        return bestScore >= 0 ? best : null;
    }

    private static int ScoreSiteMatch(string canonicalUrl, string urlPath, ProspectSiteEntry entry)
    {
        if (string.Equals(canonicalUrl, entry.CanonicalWebsite, StringComparison.OrdinalIgnoreCase))
        {
            return 300;
        }

        if (!entry.IsDomainRoot
            && urlPath.StartsWith(entry.PathPrefix, StringComparison.OrdinalIgnoreCase)
            && (urlPath.Length == entry.PathPrefix.Length
                || urlPath[entry.PathPrefix.Length] == '/'))
        {
            return 200 + entry.PathPrefix.Length;
        }

        if (entry.IsDomainRoot)
        {
            return 100;
        }

        return -1;
    }

    private static string ExtractPath(string canonicalUrl)
    {
        if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var uri))
        {
            return "/";
        }

        var path = uri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        return path.TrimEnd('/');
    }

    private static KnownProspectReuseDecision Decide(
        Prospect prospect,
        DateTimeOffset nowUtc,
        int evidenceReuseMaxAgeDays)
    {
        var decision = new KnownProspectReuseDecision
        {
            ProspectId = prospect.Id,
            ProspectStatus = prospect.Status
        };

        var maxAge = TimeSpan.FromDays(Math.Clamp(evidenceReuseMaxAgeDays, 1, 365));
        var age = nowUtc - prospect.UpdatedAtUtc;
        var hasContact = !string.IsNullOrWhiteSpace(prospect.Email)
                         || !string.IsNullOrWhiteSpace(prospect.WhatsApp)
                         || !string.IsNullOrWhiteSpace(prospect.Phone);
        var evidenceFresh = age <= maxAge;

        if (prospect.Status == ProspectStatus.DoNotContact)
        {
            decision.SkipFetch = true;
            decision.ReuseEvidence = true;
            decision.ReasonCode = "reuse.do_not_contact";
            decision.Explanation = "Domain is marked do-not-contact; fetch skipped.";
            return decision;
        }

        if (prospect.Status == ProspectStatus.Contacted && hasContact && evidenceFresh)
        {
            decision.SkipFetch = true;
            decision.ReuseEvidence = true;
            decision.ReasonCode = "reuse.contacted_fresh";
            decision.Explanation =
                "Previously contacted prospect with fresh stored evidence; fetch skipped to preserve budget.";
            return decision;
        }

        if (prospect.Status is ProspectStatus.ReadyForOutreach or ProspectStatus.Qualified
            && hasContact
            && evidenceFresh)
        {
            decision.SkipFetch = true;
            decision.ReuseEvidence = true;
            decision.ReasonCode = "reuse.qualified_fresh";
            decision.Explanation =
                "Already qualified prospect with fresh contact evidence; fetch skipped.";
            return decision;
        }

        if (!evidenceFresh || !hasContact)
        {
            decision.SkipFetch = false;
            decision.ReuseEvidence = false;
            decision.ReasonCode = "reuse.refetch_required";
            decision.Explanation = evidenceFresh
                ? "Known prospect lacks contact evidence; refetch allowed."
                : "Stored evidence is stale; refetch allowed for verification.";
            return decision;
        }

        decision.ReasonCode = "reuse.known_continue";
        decision.Explanation = "Known prospect will continue through normal crawl.";
        return decision;
    }
}
