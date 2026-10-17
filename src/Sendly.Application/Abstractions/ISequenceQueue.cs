namespace Sendly.Application.Abstractions;

public interface ISequenceQueue
{
    ValueTask EnqueueAsync(Guid enrollmentId, CancellationToken cancellationToken);

    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}
