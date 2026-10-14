using IncidentHub.Application.Dashboard.Dtos;
using MediatR;

namespace IncidentHub.Application.Dashboard.GetAppStatus;

public sealed record GetAppStatusQuery : IRequest<IReadOnlyList<AppStatusItemDto>>;
