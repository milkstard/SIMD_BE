using MediatR;

namespace IncidentHub.Application.Apps.GetApp;

public sealed record GetAppQuery(Guid Id) : IRequest<ApplicationDto>;
