using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class SeedProspectSource : IProspectSource
{
    private readonly ResearchOptions _options;

    public SeedProspectSource(IOptions<ResearchOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "Seed";

    public bool IsEnabled => _options.EnableSeedProvider && _options.SeedCandidates.Count > 0;

    public Task<IReadOnlyList<ProspectCandidate>> DiscoverAsync(
        ProspectSourceContext context,
        CancellationToken cancellationToken)
    {
        if (context.DiscoveryState.CompletedSourceNames.Contains(Name))
        {
            return Task.FromResult<IReadOnlyList<ProspectCandidate>>([]);
        }

        var result = new List<ProspectCandidate>();
        foreach (var seed in _options.SeedCandidates)
        {
            if (string.IsNullOrWhiteSpace(seed.CompanyName))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(seed.SourceUrl) && string.IsNullOrWhiteSpace(seed.SourceName))
            {
                continue;
            }

            result.Add(new ProspectCandidate
            {
                CompanyName = seed.CompanyName,
                Website = seed.Website,
                Country = seed.Country,
                City = seed.City,
                Industry = seed.Industry,
                CompanyDescription = seed.CompanyDescription,
                CompanySize = seed.CompanySize,
                ContactName = seed.ContactName,
                ContactRole = seed.ContactRole,
                Email = seed.Email,
                Phone = seed.Phone,
                WhatsApp = seed.WhatsApp,
                LinkedInUrl = seed.LinkedInUrl,
                SourceUrl = seed.SourceUrl,
                SourceType = ProspectSourceType.Import,
                SourceName = seed.SourceName ?? "Configured seed source",
                ResearchNotes = $"Matched project '{context.ProjectName}' via configured research seed provider."
            });
        }

        context.DiscoveryState.CompletedSourceNames.Add(Name);
        context.DiscoveryState.CandidatesDiscovered += result.Count;
        return Task.FromResult<IReadOnlyList<ProspectCandidate>>(result);
    }
}
