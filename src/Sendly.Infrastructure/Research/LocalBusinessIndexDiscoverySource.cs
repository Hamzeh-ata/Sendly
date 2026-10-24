using Sendly.Application.Abstractions;
using Sendly.Application.Services;

namespace Sendly.Infrastructure.Research;

public class LocalBusinessIndexDiscoverySource : IUrlDiscoverySource
{
    private readonly LocalBusinessIndexService _index;

    public LocalBusinessIndexDiscoverySource(LocalBusinessIndexService index)
    {
        _index = index;
    }

    public string Name => "LocalBusinessIndex";

    public bool IsEnabled(UrlDiscoveryContext context) => context.EnablePriorResearchUrls;

    public Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
        => _index.ToDiscoveryCandidatesAsync(
            context.ProjectId,
            context.Countries,
            context.Industries,
            Math.Clamp(context.MaxUrls / 4, 10, 60),
            cancellationToken);
}
