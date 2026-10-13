using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure.Attachments;

namespace IncidentHub.Worker.Jobs;

/// <summary>
/// Hourly, removes uploads that were never linked to a report or comment within 24 hours. A Redis lock keeps it to one
/// instance at a time.
/// </summary>
public sealed class OrphanAttachmentJob(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    ILogger<OrphanAttachmentJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private const string LockName = "orphan-attachments";

    private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Orphan attachment sweep failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var held = await locks.TryAcquireAsync(LockName, LockTtl, cancellationToken);
        if (held is null)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var sweeper = scope.ServiceProvider.GetRequiredService<OrphanAttachmentSweeper>();

        // A pass removes up to one batch; keep going while batches come back full, within the lock's lifetime.
        while (await sweeper.RunOnceAsync(cancellationToken) == OrphanAttachmentSweeper.BatchSize)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
