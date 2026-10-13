using FluentValidation;
using FluentValidation.Results;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Incidents.AssignIncident;

public sealed class AssignIncidentHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAppTeamReader teams,
    TimeProvider time,
    ILogger<AssignIncidentHandler> logger) : IRequestHandler<AssignIncidentCommand, IncidentDetailDto>
{
    public async Task<IncidentDetailDto> Handle(AssignIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await IncidentWriteGuard.LoadForWriteAsync(
            db, teams, currentUser, request.IncidentId, request.ExpectedRowVersion, cancellationToken);

        // Unchanged values need no lookup, so a no-op stays cheap and cannot fail on data that changed since.
        if (request.AssigneeId is { } assigneeId
            && assigneeId != incident.AssigneeId
            && (!await db.Users.AnyAsync(u => u.Id == assigneeId, cancellationToken)
                || !await teams.IsMemberAsync(assigneeId, incident.ApplicationId, cancellationToken)))
        {
            throw Invalid(nameof(AssignIncidentCommand.AssigneeId), IncidentMessages.AssigneeNotOnTeam);
        }

        if (request.TeamId is { } teamId
            && teamId != incident.AssignedTeamId
            && !await db.Teams.AnyAsync(t => t.Id == teamId, cancellationToken))
        {
            throw Invalid(nameof(AssignIncidentCommand.TeamId), IncidentMessages.TeamNotFound);
        }

        var actor = currentUser.ToActor();
        incident.Assign(request.AssigneeId, request.TeamId, actor, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Incident {IncidentNumber} assigned to {AssigneeId} and team {TeamId}",
            new IncidentNumber(incident.Number),
            request.AssigneeId,
            request.TeamId);

        return await IncidentProjections.LoadDetailAsync(db, incident.Id, actor, cancellationToken);
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
