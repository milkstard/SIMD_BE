using System.Linq.Expressions;
using IncidentHub.Application.Common;
using IncidentHub.Domain.Teams;

namespace IncidentHub.Application.Teams;

public sealed record TeamDto(Guid Id, string Name, string Email, string? TeamsChannelUrl, string RowVersion) : IVersionedDto
{
    /// <summary>Projection used by list/get queries so aggregates are never loaded.</summary>
    public static readonly Expression<Func<Team, TeamDto>> Projection = t =>
        new TeamDto(t.Id, t.Name, t.Email, t.TeamsChannelUrl, Convert.ToBase64String(t.RowVersion));

    public static TeamDto From(Team team) =>
        new(team.Id, team.Name, team.Email, team.TeamsChannelUrl, Convert.ToBase64String(team.RowVersion));
}
