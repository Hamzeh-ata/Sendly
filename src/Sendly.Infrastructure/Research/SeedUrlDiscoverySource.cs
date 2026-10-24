using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Infrastructure.Research;

public class SeedUrlDiscoverySource : IUrlDiscoverySource
{
    private readonly ISendlyDbContext _db;

    public SeedUrlDiscoverySource(ISendlyDbContext db)
    {
        _db = db;
    }

    public string Name => "SeedUrls";

    public bool IsEnabled(UrlDiscoveryContext context) => context.ResearchQueryId is not null;

    public async Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
    {
        if (context.ResearchQueryId is null)
        {
            return [];
        }

        var seeds = await _db.ResearchSeedUrls.AsNoTracking()
            .Where(x => x.ResearchQueryId == context.ResearchQueryId.Value && x.IsActive)
            .ToListAsync(cancellationToken);

        var result = new List<DiscoveredUrlCandidate>();
        foreach (var seed in seeds)
        {
            var canonical = UrlCanonicalizer.Canonicalize(seed.Url);
            if (string.IsNullOrWhiteSpace(canonical))
            {
                continue;
            }

            result.Add(new DiscoveredUrlCandidate
            {
                Url = canonical,
                SourceType = DiscoverySourceType.SeedUrl,
                SourceName = seed.Label ?? "Project seed URL",
                Depth = 0
            });
        }

        return result;
    }
}
