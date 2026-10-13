using FluentValidation;
using FluentValidation.Results;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Incidents.TransitionIncident;

public sealed class TransitionIncidentHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAppTeamReader teams,
    TimeProvider time,
    ILogger<TransitionIncidentHandler> logger) : IRequestHandler<TransitionIncidentCommand, IncidentDetailDto>
{
    public async Task<IncidentDetailDto> Handle(TransitionIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await IncidentWriteGuard.LoadForWriteAsync(
            db, teams, currentUser, request.IncidentId, request.ExpectedRowVersion, cancellationToken);

        var actor = currentUser.ToActor();
        var from = incident.Status;

        // The domain decides first so an undefined (409) or forbidden (403) move wins over a bad original (422).
        // Nothing is saved until the check passes; a throw leaves the request-scoped context unsaved.
        incident.TransitionTo(request.To, actor, request.Note, time.GetUtcNow(), request.CloseReason, request.DuplicateOfId);

        if (request.CloseReason == CloseReason.Duplicate && request.DuplicateOfId is { } originalId)
        {
            await EnsureOriginalIsVisibleAsync(incident.Id, originalId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Incident {IncidentNumber} moved from {From} to {To}",
            new IncidentNumber(incident.Number),
            from,
            request.To);

        return await IncidentProjections.LoadDetailAsync(db, incident.Id, actor, cancellationToken);
    }

    private async Task EnsureOriginalIsVisibleAsync(Guid incidentId, Guid originalId, CancellationToken cancellationToken)
    {
        var applicationId = originalId == incidentId
            ? null
            : await db.Incidents
                .AsNoTracking()
                .Where(i => i.Id == originalId)
                .Select(i => (Guid?)i.ApplicationId)
                .FirstOrDefaultAsync(cancellationToken);

        if (applicationId is not { } id || !await teams.IsMemberAsync(currentUser.UserId, id, cancellationToken))
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(TransitionIncidentCommand.DuplicateOfId), IncidentMessages.DuplicateTargetInvalid)]);
        }
    }
}
