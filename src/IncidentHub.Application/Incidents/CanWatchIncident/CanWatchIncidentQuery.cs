using MediatR;

namespace IncidentHub.Application.Incidents.CanWatchIncident;

/// <summary>
/// Whether the user may see the incident (member of its application's team). The user is passed in because hub
/// invocations have no reliable <c>HttpContext</c>. An unknown incident and a non-member both answer <c>false</c>.
/// </summary>
public sealed record CanWatchIncidentQuery(Guid IncidentId, Guid UserId) : IRequest<bool>;
