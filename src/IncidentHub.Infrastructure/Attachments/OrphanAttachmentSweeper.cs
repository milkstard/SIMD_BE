using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Infrastructure.Attachments;

/// <summary>
/// One pass of the orphan job: deletes uploads that were never linked to a report or comment within <see cref="UnlinkedTtl"/>
/// (row and blob). The only hard delete of attachments. The Worker calls it hourly under a distributed lock; it lives here so
/// integration tests can run a single pass.
/// </summary>
public sealed class OrphanAttachmentSweeper(
    AppDbContext db,
    IFileStore files,
    TimeProvider time,
    ILogger<OrphanAttachmentSweeper> logger)
{
    public const int BatchSize = 100;

    public static readonly TimeSpan UnlinkedTtl = TimeSpan.FromHours(24);

    /// <summary>Returns the number of attachments removed in this pass.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("attachments.sweep");
        var cutoff = time.GetUtcNow() - UnlinkedTtl;

        // Oldest first, served by the filtered IX_Attachments_Unlinked index.
        var candidates = await db.Attachments.AsNoTracking()
            .Where(a => a.IncidentId == null && a.CreatedAt < cutoff)
            .OrderBy(a => a.CreatedAt)
            .Take(BatchSize)
            .Select(a => new { a.Id, a.BlobPath })
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var candidate in candidates)
        {
            // Row first, and only while it is still unlinked: a link that raced this pass either wins (the row survives and
            // keeps its blob) or loses with a 422. A linked file never loses its blob.
            var deleted = await db.Attachments
                .Where(a => a.Id == candidate.Id && a.IncidentId == null)
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted == 0)
            {
                continue;
            }

            removed++;
            try
            {
                await files.DeleteAsync(candidate.BlobPath, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The row is gone; the blob is an orphan that only costs storage.
                logger.LogWarning(exception, "Blob of orphan attachment {AttachmentId} could not be deleted", candidate.Id);
            }
        }

        if (removed > 0)
        {
            logger.LogInformation("Removed {Count} unlinked attachments older than {Ttl}", removed, UnlinkedTtl);
        }

        return removed;
    }
}
