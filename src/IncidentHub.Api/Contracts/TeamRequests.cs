namespace IncidentHub.Api.Contracts;

public sealed record CreateTeamRequest(string Name, string Email, string? TeamsChannelUrl);

/// <summary>Full replace (PUT); the expected RowVersion travels in <c>If-Match</c>.</summary>
public sealed record UpdateTeamRequest(string Name, string Email, string? TeamsChannelUrl);
