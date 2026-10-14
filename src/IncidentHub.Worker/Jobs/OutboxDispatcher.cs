using IncidentHub.Application.Outbox;
using IncidentHub.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

namespace IncidentHub.Worker.Jobs;

/// <summary>
/// Polls the outbox every <see cref="OutboxDispatcherOptions.PollInterval"/> and dispatches a batch; after a full batch it
/// loops at once. There is no Redis lock on purpose: the row claim (<c>UPDLOCK, READPAST</c>) is the mutual exclusion, so
/// several replicas may run side by side (unlike <c>SlaMonitor</c>).
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IOptions<OutboxDispatcherOptions> options,
    TimeProvider time,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                claimed = await scope.ServiceProvider.GetRequiredService<IOutboxDispatchRunner>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch pass failed");
            }

            if (claimed >= settings.BatchSize)
            {
                continue;
            }

            try
            {
                await Task.Delay(settings.PollInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
