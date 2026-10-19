using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Sla.EvaluateSla;

/// <summary>
/// Flags open incidents whose resolve window reached the warning percentage or passed its due time. Each incident is saved on its
/// own, so the history and outbox rows of a change commit with it and one failing incident does not stop the rest.
/// </summary>
public sealed class EvaluateSlaHandler(IAppDbContext db, TimeProvider time, ILogger<EvaluateSlaHandler> logger)
    : IRequestHandler<EvaluateSlaCommand, EvaluateSlaResult>
{
    public const int BatchSize = 100;

    public async Task<EvaluateSlaResult> Handle(EvaluateSlaCommand request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        int warned = 0, breached = 0, failed = 0;
        DateTimeOffset? lastDue = null;
        var lastId = Guid.Empty;

        while (true)
        {
            var batch = await CandidatesAsync(now, lastDue, lastId, cancellationToken);

            foreach (var candidate in batch)
            {
                switch (await EvaluateAsync(candidate.Id, now, cancellationToken))
                {
                    case Outcome.Warned:
                        warned++;
                        break;
                    case Outcome.Breached:
                        breached++;
                        break;
                    case Outcome.Failed:
                        failed++;
                        break;
                }
            }

            if (batch.Count < BatchSize)
            {
                break;
            }

            lastDue = batch[^1].ResolveDueAt;
            lastId = batch[^1].Id;
        }

        if (warned + breached + failed > 0)
        {
            logger.LogInformation(
                "SLA evaluation: {Warned} warned, {Breached} breached, {Failed} failed",
                warned,
                breached,
                failed);
        }

        return new EvaluateSlaResult(warned, breached, failed);
    }

    private async Task<List<Candidate>> CandidatesAsync(
        DateTimeOffset now,
        DateTimeOffset? afterDue,
        Guid afterId,
        CancellationToken cancellationToken)
    {
        var query = db.Incidents
            .AsNoTracking()
            .Where(i => !i.IsSlaBreached
                && i.Status != IncidentStatus.Resolved
                && i.Status != IncidentStatus.Closed
                && i.ResolveDueAt != null
                && (i.ResolveDueAt <= now || (i.SlaWarnedAt == null && i.SlaWarnAt <= now)));

        if (afterDue is { } due)
        {
            query = query.Where(i => i.ResolveDueAt > due || (i.ResolveDueAt == due && i.Id.CompareTo(afterId) > 0));
        }

        return await query
            .OrderBy(i => i.ResolveDueAt)
            .ThenBy(i => i.Id)
            .Select(i => new Candidate(i.Id, i.ResolveDueAt!.Value))
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task<Outcome> EvaluateAsync(Guid incidentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);
            if (incident is null)
            {
                return Outcome.Skipped;
            }

            // Decided again on the tracked row: a user may have resolved or re-rated it since the candidate read.
            var action = SlaEvaluation.Decide(incident, now);
            switch (action)
            {
                case SlaAction.Breach:
                    incident.MarkSlaBreached(now);
                    break;
                case SlaAction.Warn:
                    incident.MarkSlaWarned(now);
                    break;
                default:
                    return Outcome.Skipped;
            }

            await db.SaveChangesAsync(cancellationToken);

            var number = new IncidentNumber(incident.Number);
            if (action == SlaAction.Breach)
            {
                logger.LogWarning("Incident {IncidentNumber} SLA breached", number);
                return Outcome.Breached;
            }

            logger.LogInformation("Incident {IncidentNumber} SLA warning raised", number);
            return Outcome.Warned;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception, "Incident {IncidentId} changed during SLA evaluation; retrying on the next pass", incidentId);
            return Outcome.Failed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "SLA evaluation of incident {IncidentId} failed", incidentId);
            return Outcome.Failed;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private sealed record Candidate(Guid Id, DateTimeOffset ResolveDueAt);

    private enum Outcome
    {
        Skipped,
        Warned,
        Breached,
        Failed,
    }
}
