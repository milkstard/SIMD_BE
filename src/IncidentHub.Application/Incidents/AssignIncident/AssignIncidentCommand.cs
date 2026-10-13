using IncidentHub.Application.Incidents.Dtos;
using MediatR;

namespace IncidentHub.Application.Incidents.AssignIncident;

/// <param name="AssigneeId">The user to assign, or <c>null</c> to unassign.</param>
/// <param name="TeamId">The team to assign, or <c>null</c> to clear it.</param>
/// <param name="ExpectedRowVersion">The decoded <c>If-Match</c> value the client last saw.</param>
public sealed record AssignIncidentCommand(
    Guid IncidentId,
    Guid? AssigneeId,
    Guid? TeamId,
    byte[] ExpectedRowVersion) : IRequest<IncidentDetailDto>;
