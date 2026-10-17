using System.Threading.Channels;
using Sendly.Application.Abstractions;

namespace Sendly.Infrastructure.Jobs;

public class InMemorySequenceQueue : ISequenceQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(enrollmentId, cancellationToken);
    }

    public async IAsyncEnumerable<Guid> DequeueAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var enrollmentId in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return enrollmentId;
        }
    }
}
