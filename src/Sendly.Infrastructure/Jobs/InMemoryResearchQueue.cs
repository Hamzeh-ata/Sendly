using System.Threading.Channels;
using Sendly.Application.Abstractions;

namespace Sendly.Infrastructure.Jobs;

public class InMemoryResearchQueue : IResearchQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(researchRunId, cancellationToken);
    }

    public async IAsyncEnumerable<Guid> DequeueAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var researchRunId in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return researchRunId;
        }
    }
}
