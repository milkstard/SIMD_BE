using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure.Attachments;

namespace IncidentHub.Worker.Jobs;

/// <summary>
/// Every 10 seconds, scans the attachments that are still <c>Pending</c>. A Redis lock keeps it to one instance at a time;
/// the pass itself also skips rows another instance has claimed (<c>UPDLOCK, READPAST</c>).
/// </summary>
public sealed class AttachmentScanJob(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    ILogger<AttachmentScanJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private const string LockName = "attachment-scan";

    // Longer than a pass of 20 files, short enough that a crashed holder blocks the next one only briefly.
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(60);

    // No new file is started after this, so the pass (one SQL transaction with row locks) ends before the lock expires
    // and a second instance never scans the same rows. One file is bounded by the storage client's own timeouts.
    private static readonly TimeSpan PassBudget = TimeSpan.FromSeconds(30);

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
                logger.LogError(exception, "Attachment scan pass failed");
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
        await scope.ServiceProvider.GetRequiredService<AttachmentScanRunner>().RunOnceAsync(cancellationToken, PassBudget);
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
