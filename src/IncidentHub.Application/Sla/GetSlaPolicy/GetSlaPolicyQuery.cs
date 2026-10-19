using MediatR;

namespace IncidentHub.Application.Sla.GetSlaPolicy;

public sealed record GetSlaPolicyQuery(string Id) : IRequest<SlaPolicyDto>;
