namespace Sendly.Application.Abstractions;

public interface IResearchQueue
{
    ValueTask EnqueueAsync(Guid researchRunId, CancellationToken cancellationToken);

    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}
