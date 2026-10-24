using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class DiscoverySourceRegistryService
{
    private readonly ISendlyDbContext _db;

    public DiscoverySourceRegistryService(ISendlyDbContext db)
    {
        _db = db;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.DiscoverySources
            .Where(x => x.IsSystemManaged)
            .ToListAsync(cancellationToken);
        var byCanonical = new Dictionary<string, DiscoverySource>(StringComparer.OrdinalIgnoreCase);
        var byDomain = new Dictionary<string, DiscoverySource>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in existing)
        {
            if (!string.IsNullOrWhiteSpace(source.NormalizedBaseUrl))
            {
                byCanonical[source.NormalizedBaseUrl] = source;
            }

            var domain = source.NormalizedDomain ?? UrlCanonicalizer.ExtractDomain(source.NormalizedBaseUrl);
            if (!string.IsNullOrWhiteSpace(domain) && !byDomain.ContainsKey(domain))
            {
                byDomain[domain] = source;
            }
        }

        foreach (var entry in SystemDirectoryCatalog.AllEntries())
        {
            var canonical = UrlCanonicalizer.Canonicalize(entry.BaseUrl);
            if (string.IsNullOrWhiteSpace(canonical))
            {
                continue;
            }

            var domain = UrlCanonicalizer.ExtractDomain(canonical);
            if (byCanonical.TryGetValue(canonical, out var exact)
                || (!string.IsNullOrWhiteSpace(domain) && byDomain.TryGetValue(domain, out exact)))
            {
                exact.Name = entry.Name;
                exact.BaseUrl = entry.BaseUrl;
                exact.NormalizedBaseUrl = canonical;
                exact.NormalizedDomain = domain;
                exact.AdapterKey = entry.DiscoveryMethod;
                exact.CountriesJson = JsonSerializer.Serialize(entry.Countries);
                exact.IndustriesJson = JsonSerializer.Serialize(entry.Industries);
                exact.UpdatedAtUtc = DateTimeOffset.UtcNow;
                if (exact.Status == DiscoverySourceStatus.Disabled)
                {
                    exact.Status = DiscoverySourceStatus.Active;
                }

                byCanonical[canonical] = exact;
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    byDomain[domain] = exact;
                }

                continue;
            }

            var created = new DiscoverySource
            {
                Name = entry.Name,
                BaseUrl = entry.BaseUrl,
                NormalizedBaseUrl = canonical,
                NormalizedDomain = domain,
                SourceType = DiscoverySourceType.DirectoryCatalog,
                AdapterKey = entry.DiscoveryMethod,
                CountriesJson = JsonSerializer.Serialize(entry.Countries),
                IndustriesJson = JsonSerializer.Serialize(entry.Industries),
                AutomatedAccessPermitted = true,
                Status = DiscoverySourceStatus.Active,
                IsSystemManaged = true,
                ProvenanceNotes = "Seeded from SystemDirectoryCatalog",
                RelevanceScore = 40,
                FreshnessScore = 50,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _db.DiscoverySources.Add(created);
            byCanonical[canonical] = created;
            if (!string.IsNullOrWhiteSpace(domain))
            {
                byDomain[domain] = created;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<DiscoverySource>> ResolveEligibleAsync(
        IReadOnlyList<string> countries,
        IReadOnlyList<string> industries,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveEligibleWithDecisionsAsync(countries, industries, cancellationToken);
        return resolved.Sources;
    }

    public async Task<DiscoverySourceResolveResult> ResolveEligibleWithDecisionsAsync(
        IReadOnlyList<string> countries,
        IReadOnlyList<string> industries,
        CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var all = await _db.DiscoverySources
            .Where(x => x.Status == DiscoverySourceStatus.Active
                        || x.Status == DiscoverySourceStatus.TemporarilyDeprioritized)
            .ToListAsync(cancellationToken);

        var inputs = new List<(string Name, string? Url, string Kind, IReadOnlyList<string> Countries, IReadOnlyList<string> Industries, bool AccessPermitted, bool TemporarilyUnavailable, DiscoverySource Entity)>();
        foreach (var source in all)
        {
            var temporarilyUnavailable = source.Status == DiscoverySourceStatus.TemporarilyDeprioritized
                                         && source.NextEligibleRetryAtUtc is not null
                                         && source.NextEligibleRetryAtUtc > now;
            var kind = ResolveSourceKind(source);
            inputs.Add((
                source.Name,
                source.NormalizedBaseUrl,
                kind,
                DeserializeList(source.CountriesJson),
                DeserializeList(source.IndustriesJson),
                source.AutomatedAccessPermitted,
                temporarilyUnavailable,
                source));
        }

        var eligibility = SourceEligibilityEvaluator.ResolveMany(
            inputs.Select(x => (
                x.Name,
                x.Url,
                x.Kind,
                x.Countries,
                x.Industries,
                x.AccessPermitted,
                x.TemporarilyUnavailable)),
            countries,
            industries);

        var matched = new List<(DiscoverySource Source, SourceEligibilityDecision Decision)>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var decision = eligibility.Decisions[i];
            if (decision.State is SourceEligibilityState.Eligible or SourceEligibilityState.EligibleSecondary)
            {
                matched.Add((inputs[i].Entity, decision));
            }
        }

        var ordered = matched
            .OrderByDescending(x => x.Decision.CountsAsRelevantIndustrySource)
            .ThenByDescending(x => x.Decision.ExpectedDiscoveryValue)
            .ThenByDescending(x => ComputePriorityScore(x.Source))
            .ThenBy(x => x.Source.Name)
            .ToList();

        return new DiscoverySourceResolveResult
        {
            Sources = ordered.Select(x => x.Source).ToList(),
            Decisions = eligibility.Decisions,
            Eligibility = eligibility,
            SourceDecisions = ordered.ToDictionary(x => x.Source.Id, x => x.Decision)
        };
    }

    private static string ResolveSourceKind(DiscoverySource source)
    {
        if (source.SourceType == DiscoverySourceType.IndustryAssociation)
        {
            return "Association";
        }

        var catalog = SystemDirectoryCatalog.AllEntries()
            .FirstOrDefault(x => string.Equals(
                UrlCanonicalizer.Canonicalize(x.BaseUrl),
                source.NormalizedBaseUrl,
                StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(catalog?.SourceKind))
        {
            return catalog.SourceKind;
        }

        var name = source.Name ?? string.Empty;
        if (name.Contains("OpenCorporates", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Companies House", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Registry", StringComparison.OrdinalIgnoreCase))
        {
            return "Registry";
        }

        if (name.Contains("Association", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Council", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Federation", StringComparison.OrdinalIgnoreCase))
        {
            return "Association";
        }

        return "Directory";
    }

    public async Task RecordAttemptAsync(
        Guid sourceId,
        bool succeeded,
        int candidates,
        string? error,
        CancellationToken cancellationToken)
    {
        var source = await _db.DiscoverySources.FirstOrDefaultAsync(x => x.Id == sourceId, cancellationToken);
        if (source is null)
        {
            return;
        }

        source.AttemptCount++;
        source.LastAttemptedAtUtc = DateTimeOffset.UtcNow;
        source.LastCheckedAtUtc = DateTimeOffset.UtcNow;
        source.LastFreshnessAssessedAtUtc = DateTimeOffset.UtcNow;
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (succeeded)
        {
            source.SuccessCount++;
            source.LastSucceededAtUtc = DateTimeOffset.UtcNow;
            source.CandidatesDiscovered += Math.Max(0, candidates);
            source.LastError = null;
            source.FreshnessScore = Math.Min(100, source.FreshnessScore + 1);
            if (source.Status == DiscoverySourceStatus.TemporarilyDeprioritized)
            {
                source.Status = DiscoverySourceStatus.Active;
                source.NextEligibleRetryAtUtc = null;
            }
        }
        else
        {
            source.FailureCount++;
            source.LastError = Truncate(error, 1000);
            source.FreshnessScore = Math.Max(0, source.FreshnessScore - 5);
            if (source.FailureCount >= 3 && source.SuccessCount == 0)
            {
                source.Status = DiscoverySourceStatus.TemporarilyDeprioritized;
                source.NextEligibleRetryAtUtc = DateTimeOffset.UtcNow.AddHours(6);
            }
        }

        source.RelevanceScore = ComputePriorityScore(source);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateConfigurationAsync(
        Guid sourceId,
        string configurationJson,
        CancellationToken cancellationToken)
    {
        var source = await _db.DiscoverySources.FirstOrDefaultAsync(x => x.Id == sourceId, cancellationToken);
        if (source is null)
        {
            return;
        }

        source.ConfigurationJson = Truncate(configurationJson, 4000);
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordFunnelEventAsync(
        Guid? sourceId,
        bool accessible,
        bool parsed,
        bool relevantCompany,
        bool liveVerified,
        bool publicContact,
        bool duplicate,
        long processingMs,
        CancellationToken cancellationToken)
    {
        if (sourceId is null)
        {
            return;
        }

        var source = await _db.DiscoverySources.FirstOrDefaultAsync(x => x.Id == sourceId.Value, cancellationToken);
        if (source is null)
        {
            return;
        }

        source.LastCheckedAtUtc = DateTimeOffset.UtcNow;
        source.TotalProcessingMs += Math.Max(0, processingMs);
        if (accessible)
        {
            source.PagesAccessible++;
        }

        if (parsed)
        {
            source.PagesParsed++;
        }

        if (relevantCompany)
        {
            source.RelevantCompaniesDiscovered++;
        }

        if (liveVerified)
        {
            source.LiveVerifiedCompanies++;
        }

        if (publicContact)
        {
            source.PublicContactsExtracted++;
        }

        if (duplicate)
        {
            source.DuplicateHits++;
        }

        source.RelevanceScore = ComputePriorityScore(source);
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordQualificationYieldAsync(
        string? sourceUrl,
        bool qualified,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return;
        }

        var domain = UrlCanonicalizer.ExtractDomain(sourceUrl);
        if (string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        var sources = await _db.DiscoverySources
            .Where(x => x.NormalizedDomain == domain)
            .ToListAsync(cancellationToken);
        foreach (var source in sources)
        {
            source.UniqueCompaniesDiscovered++;
            if (qualified)
            {
                source.QualifiedProspectsYielded++;
            }

            source.RelevanceScore = ComputePriorityScore(source);
            source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        if (sources.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<DiscoverySource?> TryRegisterExpandedSourceAsync(
        string name,
        string url,
        DiscoverySourceType sourceType,
        IReadOnlyList<string> countries,
        IReadOnlyList<string> industries,
        Guid? parentSourceId,
        string provenance,
        CancellationToken cancellationToken)
    {
        var canonical = UrlCanonicalizer.Canonicalize(url);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return null;
        }

        if (!Uri.TryCreate(canonical, UriKind.Absolute, out var uri)
            || await SsrfGuard.IsBlockedUriAsync(uri, cancellationToken))
        {
            return null;
        }

        var existing = await _db.DiscoverySources
            .FirstOrDefaultAsync(x => x.NormalizedBaseUrl == canonical, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var source = new DiscoverySource
        {
            Name = string.IsNullOrWhiteSpace(name) ? uri.Host : name.Trim(),
            BaseUrl = canonical,
            NormalizedBaseUrl = canonical,
            NormalizedDomain = UrlCanonicalizer.ExtractDomain(canonical),
            SourceType = sourceType,
            AdapterKey = "DirectoryListing",
            CountriesJson = JsonSerializer.Serialize(countries.ToList()),
            IndustriesJson = JsonSerializer.Serialize(industries.ToList()),
            AutomatedAccessPermitted = true,
            Status = DiscoverySourceStatus.CandidatePending,
            ParentDiscoverySourceId = parentSourceId,
            ProvenanceNotes = Truncate(provenance, 2000),
            RelevanceScore = 15,
            FreshnessScore = 40,
            IsSystemManaged = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.DiscoverySources.Add(source);
        await _db.SaveChangesAsync(cancellationToken);
        return source;
    }

    public async Task PromoteCandidateIfUsefulAsync(
        Guid sourceId,
        bool useful,
        string? reason,
        CancellationToken cancellationToken)
    {
        var source = await _db.DiscoverySources.FirstOrDefaultAsync(x => x.Id == sourceId, cancellationToken);
        if (source is null || source.Status != DiscoverySourceStatus.CandidatePending)
        {
            return;
        }

        if (useful)
        {
            source.Status = DiscoverySourceStatus.Active;
            source.LastSucceededAtUtc = DateTimeOffset.UtcNow;
            source.RelevanceScore = Math.Max(source.RelevanceScore, 25);
        }
        else
        {
            source.Status = DiscoverySourceStatus.EvaluationFailed;
            source.LastError = Truncate(reason, 1000);
        }

        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<DiscoverySource>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(cancellationToken);
        var items = await _db.DiscoverySources.AsNoTracking().ToListAsync(cancellationToken);
        return items
            .OrderByDescending(x => x.RelevanceScore)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public static double ComputePriorityScore(DiscoverySource source)
    {
        var yieldRate = source.AttemptCount == 0
            ? 0
            : (double)source.CandidatesDiscovered / source.AttemptCount;
        var qualificationRate = source.UniqueCompaniesDiscovered == 0
            ? 0
            : (double)source.QualifiedProspectsYielded / source.UniqueCompaniesDiscovered;
        var availability = source.AttemptCount == 0
            ? 0.5
            : (double)source.SuccessCount / source.AttemptCount;
        var liveRate = source.PagesAccessible == 0
            ? 0
            : (double)source.LiveVerifiedCompanies / source.PagesAccessible;
        var contactRate = source.RelevantCompaniesDiscovered == 0
            ? 0
            : (double)source.PublicContactsExtracted / source.RelevantCompaniesDiscovered;

        return Math.Round(
            (availability * 25)
            + (Math.Min(yieldRate, 20) * 1.5)
            + (qualificationRate * 20)
            + (liveRate * 15)
            + (contactRate * 10)
            + (source.FreshnessScore * 0.15)
            + (source.SourceType == DiscoverySourceType.IndustryAssociation ? 8 : 0),
            2);
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length <= max ? value : value[..max];
    }
}
