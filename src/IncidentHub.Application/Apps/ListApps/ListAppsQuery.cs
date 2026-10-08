using IncidentHub.Application.Common.Paging;
using MediatR;

namespace IncidentHub.Application.Apps.ListApps;

public sealed record ListAppsQuery(string? Cursor, int Limit = PageLimits.Default, bool IncludeInactive = false)
    : IRequest<Paged<ApplicationDto>>;
