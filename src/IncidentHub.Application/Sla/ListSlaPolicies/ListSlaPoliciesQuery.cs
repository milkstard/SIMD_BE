using MediatR;

namespace IncidentHub.Application.Sla.ListSlaPolicies;

public sealed record ListSlaPoliciesQuery : IRequest<IReadOnlyList<SlaPolicyDto>>;
