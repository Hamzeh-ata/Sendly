using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sendly.Infrastructure.Jobs;

public abstract class EfDurableWorkQueue
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly string _queueName;
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected EfDurableWorkQueue(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string queueName)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _queueName = queueName;
    }

    protected async ValueTask EnqueueCoreAsync(Guid payloadId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SendlyDbContext>();
        var exists = await db.BackgroundJobWorkItems.AnyAsync(
            x => x.QueueName == _queueName
                 && x.PayloadId == payloadId
                 && x.CompletedAtUtc == null,
            cancellationToken);
        if (exists)
        {
            return;
        }

        db.BackgroundJobWorkItems.Add(new BackgroundJobWorkItem
        {
            QueueName = _queueName,
            PayloadId = payloadId,
            AvailableAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    protected async IAsyncEnumerable<Guid> DequeueAllCoreAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid? previousId = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (previousId is not null)
            {
                await CompleteAsync(previousId.Value, cancellationToken);
                previousId = null;
            }

            var claimed = await ClaimNextAsync(cancellationToken);
            if (claimed is null)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                continue;
            }

            previousId = claimed.Value.ItemId;
            yield return claimed.Value.PayloadId;
        }

        if (previousId is not null)
        {
            await CompleteAsync(previousId.Value, cancellationToken);
        }
    }

    private async Task<(Guid ItemId, Guid PayloadId)?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SendlyDbContext>();
        var now = DateTimeOffset.UtcNow;
        var pending = await db.BackgroundJobWorkItems
            .Where(x =>
                x.QueueName == _queueName
                && x.CompletedAtUtc == null)
            .ToListAsync(cancellationToken);
        var candidates = pending
            .Where(x =>
                x.AvailableAtUtc <= now
                && (x.LockedUntilUtc == null || x.LockedUntilUtc < now))
            .OrderBy(x => x.CreatedAtUtc)
            .Take(5)
            .ToList();

        foreach (var item in candidates)
        {
            item.LockedUntilUtc = now.AddMinutes(5);
            item.LockedBy = _workerId;
            item.AttemptCount += 1;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return (item.Id, item.PayloadId);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogDebug("Lost claim race for work item {ItemId}", item.Id);
            }
            catch (DbUpdateException)
            {
                _logger.LogDebug("Failed to claim work item {ItemId}", item.Id);
            }
        }

        return null;
    }

    private async Task CompleteAsync(Guid itemId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SendlyDbContext>();
        var item = await db.BackgroundJobWorkItems.FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken);
        if (item is null || item.CompletedAtUtc is not null)
        {
            return;
        }

        item.CompletedAtUtc = DateTimeOffset.UtcNow;
        item.LockedUntilUtc = null;
        item.LockedBy = null;
        await db.SaveChangesAsync(cancellationToken);
    }
}

public class EfCampaignQueue : EfDurableWorkQueue, ICampaignQueue
{
    public EfCampaignQueue(IServiceScopeFactory scopeFactory, ILogger<EfCampaignQueue> logger)
        : base(scopeFactory, logger, "campaign")
    {
    }

    public ValueTask EnqueueAsync(Guid campaignId, CancellationToken cancellationToken)
        => EnqueueCoreAsync(campaignId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken)
        => DequeueAllCoreAsync(cancellationToken);
}

public class EfResearchQueue : EfDurableWorkQueue, IResearchQueue
{
    public EfResearchQueue(IServiceScopeFactory scopeFactory, ILogger<EfResearchQueue> logger)
        : base(scopeFactory, logger, "research")
    {
    }

    public ValueTask EnqueueAsync(Guid researchRunId, CancellationToken cancellationToken)
        => EnqueueCoreAsync(researchRunId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken)
        => DequeueAllCoreAsync(cancellationToken);
}

public class EfSequenceQueue : EfDurableWorkQueue, ISequenceQueue
{
    public EfSequenceQueue(IServiceScopeFactory scopeFactory, ILogger<EfSequenceQueue> logger)
        : base(scopeFactory, logger, "sequence")
    {
    }

    public ValueTask EnqueueAsync(Guid enrollmentId, CancellationToken cancellationToken)
        => EnqueueCoreAsync(enrollmentId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken)
        => DequeueAllCoreAsync(cancellationToken);
}
