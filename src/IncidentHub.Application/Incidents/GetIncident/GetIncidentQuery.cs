using IncidentHub.Application.Incidents.Dtos;
using MediatR;

namespace IncidentHub.Application.Incidents.GetIncident;

/// <param name="Number">The display number, e.g. <c>INC-1042</c>.</param>
public sealed record GetIncidentQuery(string Number) : IRequest<IncidentDetailDto>;
