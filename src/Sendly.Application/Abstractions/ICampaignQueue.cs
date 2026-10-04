namespace Sendly.Application.Abstractions;

public interface ICampaignQueue
{
    ValueTask EnqueueAsync(Guid campaignId, CancellationToken cancellationToken);

    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}
