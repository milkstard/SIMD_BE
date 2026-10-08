using IncidentHub.Application.Common.Paging;
using MediatR;

namespace IncidentHub.Application.Teams.ListTeams;

public sealed record ListTeamsQuery(string? Cursor, int Limit = PageLimits.Default, bool IncludeTotal = false)
    : IRequest<Paged<TeamDto>>;
