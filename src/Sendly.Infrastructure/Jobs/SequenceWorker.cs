using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sendly.Infrastructure.Jobs;

public class SequenceWorker : BackgroundService
{
    private readonly ISequenceQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SequenceWorker> _logger;

    public SequenceWorker(
        ISequenceQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<SequenceWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var enrollmentId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<SequenceProcessor>();
                await processor.ProcessAsync(enrollmentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sequence worker failed for {EnrollmentId}", enrollmentId);
            }
        }
    }
}
