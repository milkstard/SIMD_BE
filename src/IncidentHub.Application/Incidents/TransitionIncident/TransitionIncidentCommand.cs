using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using MediatR;

namespace IncidentHub.Application.Incidents.TransitionIncident;

/// <param name="ExpectedRowVersion">The decoded <c>If-Match</c> value the client last saw.</param>
public sealed record TransitionIncidentCommand(
    Guid IncidentId,
    IncidentStatus To,
    string? Note,
    CloseReason? CloseReason,
    Guid? DuplicateOfId,
    byte[] ExpectedRowVersion) : IRequest<IncidentDetailDto>;
