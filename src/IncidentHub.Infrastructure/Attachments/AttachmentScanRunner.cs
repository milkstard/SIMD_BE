using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Infrastructure.Attachments;

/// <summary>
/// One pass of the scan job: claims <c>Pending</c> attachments, scans each blob and records the result.
/// The Worker calls it on a timer under a distributed lock; it lives here so integration tests can run a single pass.
/// </summary>
public sealed class AttachmentScanRunner(
    AppDbContext db,
    IFileStore files,
    IAttachmentScanner scanner,
    TimeProvider time,
    ILogger<AttachmentScanRunner> logger)
{
    public const int BatchSize = 20;

    public static readonly TimeSpan PendingWarningAge = TimeSpan.FromHours(1);

    /// <summary>
    /// Returns the number of attachments that got a result in this pass. After <paramref name="budget"/> no further file is
    /// started (the ones done are still saved), so a pass cannot outlive the lock the caller holds around it.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken, TimeSpan? budget = null)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("attachments.scan");
        var scanned = await ScanBatchAsync(budget ?? Timeout.InfiniteTimeSpan, cancellationToken);
        await WarnAboutStuckAttachmentAsync(cancellationToken);
        return scanned;
    }

    private async Task<int> ScanBatchAsync(TimeSpan budget, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // UPDLOCK + READPAST: a second instance skips the rows this one holds instead of waiting for them.
        // Fewest failed attempts first: a file that cannot be scanned sinks behind new uploads instead of blocking them.
        var pending = await db.Attachments
            .FromSqlRaw(
                "SELECT TOP (" + BatchSize + ") * FROM Attachments WITH (UPDLOCK, READPAST, ROWLOCK) " +
                "WHERE ScanStatus = 'Pending' ORDER BY ScanAttempts, CreatedAt")
            .ToListAsync(cancellationToken);

        var scanned = 0;
        foreach (var attachment in pending)
        {
            if (budget != Timeout.InfiniteTimeSpan && time.GetElapsedTime(started) >= budget)
            {
                logger.LogInformation("Attachment scan stopped after its {Budget} budget; the rest wait for the next pass", budget);
                break;
            }

            try
            {
                await using var content = await files.OpenReadAsync(attachment.BlobPath, cancellationToken);
                var result = await scanner.ScanAsync(content, cancellationToken);
                attachment.MarkScanned(result);
                scanned++;

                logger.LogInformation("Attachment {AttachmentId} scanned: {ScanStatus}", attachment.Id, result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The row stays Pending and is tried again on a later pass, after the rows that have not failed.
                attachment.RecordScanFailure();
                logger.LogInformation(
                    exception,
                    "Attachment {AttachmentId} could not be scanned (attempt {Attempts})",
                    attachment.Id,
                    attachment.ScanAttempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return scanned;
    }

    /// <summary>
    /// Warns when the oldest attachment still <c>Pending</c> is older than <see cref="PendingWarningAge"/>, whatever the reason
    /// (unreadable blob, scanner down, Worker stopped earlier). One cheap read of the pending index per pass.
    /// </summary>
    private async Task WarnAboutStuckAttachmentAsync(CancellationToken cancellationToken)
    {
        var oldest = await db.Attachments.AsNoTracking()
            .Where(a => a.ScanStatus == ScanStatus.Pending)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (oldest is null)
        {
            return;
        }

        var age = time.GetUtcNow() - oldest.CreatedAt;
        if (age > PendingWarningAge)
        {
            logger.LogWarning("Attachment {AttachmentId} has been pending for {Age}", oldest.Id, age);
        }
    }
}
