namespace IncidentHub.Api.Hubs;

/// <summary>Sends one <c>DashboardChanged</c> to a team's dashboard group; the <see cref="DashboardThrottle"/> decides when.</summary>
public interface IDashboardBroadcaster
{
    Task SendAsync(Guid teamId, CancellationToken cancellationToken);
}
