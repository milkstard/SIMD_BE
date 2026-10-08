using MediatR;

namespace IncidentHub.Application.Teams.UpdateTeam;

/// <param name="ExpectedRowVersion">The decoded <c>If-Match</c> value the client last saw.</param>
public sealed record UpdateTeamCommand(
    Guid Id,
    string Name,
    string Email,
    string? TeamsChannelUrl,
    byte[] ExpectedRowVersion) : IRequest<TeamDto>;
