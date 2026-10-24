using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Sendly.Application.Services;

public class LocalBusinessIndexEntry
{
    public Guid ProspectId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string? OfficialDomain { get; set; }

    public string? Website { get; set; }

    public string? SourceUrl { get; set; }

    public string? Industry { get; set; }

    public string? Country { get; set; }

    public string? Email { get; set; }

    public ProspectStatus Status { get; set; }

    public DateTimeOffset FirstDiscoveredAtUtc { get; set; }

    public DateTimeOffset LastVerifiedAtUtc { get; set; }

    public bool NeedsRefresh { get; set; }
}

public class LocalBusinessIndexService : IDiscoverySourceAdapter
{
    private readonly ISendlyDbContext _db;
    private readonly ResearchOptions _options;

    public LocalBusinessIndexService(ISendlyDbContext db, IOptions<ResearchOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public DiscoverySourceCapabilities Capabilities { get; } = new()
    {
        Name = "LocalBusinessIndex",
        SourceType = DiscoverySourceType.LocalBusinessIndex,
        SupportsIndustryFilter = true,
        SupportsCountryFilter = true,
        RequiresPaidApi = false,
        RequiresHumanVerification = false,
        AccessNotes = "Reuses previously discovered prospects stored locally. Applies configurable refresh age; does not assume contacts remain valid.",
        SupportedRegions = ["local"]
    };

    public DiscoverySourceHealthReport GetHealth(DateTimeOffset nowUtc)
        => new()
        {
            Name = Capabilities.Name,
            Health = SearchProviderHealthState.Healthy,
            LastSuccessAtUtc = nowUtc,
            AccessRestrictions = ["Local database only"]
        };

    public async Task<IReadOnlyList<LocalBusinessIndexEntry>> QueryAsync(
        Guid projectId,
        IReadOnlyList<string> countries,
        IReadOnlyList<string> industries,
        int take,
        CancellationToken cancellationToken)
    {
        var maxAgeDays = Math.Clamp(_options.EvidenceReuseMaxAgeDays, 1, 365);
        var refreshBefore = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);
        var limit = Math.Clamp(take, 1, 200);
        var prospects = await _db.Prospects.AsNoTracking()
            .Where(x => x.Status != ProspectStatus.Rejected
                        && ((x.Website != null && x.Website != "") || (x.SourceUrl != null && x.SourceUrl != "")))
            .Take(limit * 4)
            .ToListAsync(cancellationToken);
        prospects.Sort((left, right) => right.UpdatedAtUtc.CompareTo(left.UpdatedAtUtc));

        var result = new List<LocalBusinessIndexEntry>();
        var seenDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prospect in prospects)
        {
            if (result.Count >= limit)
            {
                break;
            }

            if (!MatchesCountry(prospect, countries) || !MatchesIndustry(prospect, industries))
            {
                continue;
            }

            var website = prospect.Website ?? prospect.SourceUrl;
            var domain = UrlCanonicalizer.ExtractDomain(website);
            if (!string.IsNullOrWhiteSpace(domain) && !seenDomains.Add(domain))
            {
                continue;
            }

            result.Add(new LocalBusinessIndexEntry
            {
                ProspectId = prospect.Id,
                CompanyName = prospect.CompanyName,
                OfficialDomain = domain,
                Website = prospect.Website,
                SourceUrl = prospect.SourceUrl,
                Industry = prospect.Industry,
                Country = prospect.Country,
                Email = prospect.Email,
                Status = prospect.Status,
                FirstDiscoveredAtUtc = prospect.CreatedAtUtc,
                LastVerifiedAtUtc = prospect.UpdatedAtUtc,
                NeedsRefresh = prospect.UpdatedAtUtc < refreshBefore
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<DiscoveredUrlCandidate>> ToDiscoveryCandidatesAsync(
        Guid projectId,
        IReadOnlyList<string> countries,
        IReadOnlyList<string> industries,
        int take,
        CancellationToken cancellationToken)
    {
        var entries = await QueryAsync(projectId, countries, industries, take, cancellationToken);
        var result = new List<DiscoveredUrlCandidate>();
        foreach (var entry in entries)
        {
            var url = entry.Website ?? entry.SourceUrl;
            var canonical = UrlCanonicalizer.Canonicalize(url);
            if (string.IsNullOrWhiteSpace(canonical))
            {
                continue;
            }

            result.Add(new DiscoveredUrlCandidate
            {
                Url = canonical,
                SourceType = DiscoverySourceType.LocalBusinessIndex,
                SourceName = Capabilities.Name,
                Depth = 0,
                LinkText = entry.CompanyName,
                IsIndustryRelevant = true,
                DiscoveryReasonCode = entry.NeedsRefresh
                    ? "discovery.local_index_refresh"
                    : "discovery.local_index_reuse",
                PriorityBoost = entry.NeedsRefresh ? 8 : 22
            });
        }

        return result;
    }

    private static bool MatchesCountry(Prospect prospect, IReadOnlyList<string> countries)
    {
        if (countries.Count == 0 || string.IsNullOrWhiteSpace(prospect.Country))
        {
            return countries.Count == 0;
        }

        foreach (var country in countries)
        {
            if (CountryScopeHelper.AreSameCountry(prospect.Country, country))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesIndustry(Prospect prospect, IReadOnlyList<string> industries)
    {
        if (industries.Count == 0 || string.IsNullOrWhiteSpace(prospect.Industry))
        {
            return true;
        }

        return IndustrySynonymCatalog.MatchesAny(prospect.Industry, industries)
               || IndustrySynonymCatalog.MatchesAny(
                   $"{prospect.CompanyName} {prospect.CompanyDescription}",
                   industries);
    }
}
