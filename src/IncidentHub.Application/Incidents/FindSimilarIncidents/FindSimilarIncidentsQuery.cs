using IncidentHub.Application.Incidents.Dtos;
using MediatR;

namespace IncidentHub.Application.Incidents.FindSimilarIncidents;

public sealed record FindSimilarIncidentsQuery(Guid ApplicationId, string Title) : IRequest<IReadOnlyList<IncidentSummaryDto>>;
