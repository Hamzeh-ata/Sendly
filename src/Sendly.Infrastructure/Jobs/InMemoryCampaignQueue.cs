using System.Threading.Channels;
using Sendly.Application.Abstractions;

namespace Sendly.Infrastructure.Jobs;

public class InMemoryCampaignQueue : ICampaignQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(campaignId, cancellationToken);
    }

    public async IAsyncEnumerable<Guid> DequeueAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var campaignId in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return campaignId;
        }
    }
}
