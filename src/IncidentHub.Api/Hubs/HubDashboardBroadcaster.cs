using IncidentHub.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace IncidentHub.Api.Hubs;

public sealed class HubDashboardBroadcaster(
    IServiceScopeFactory scopes,
    IHubContext<IncidentsHub, IIncidentClient> hub) : IDashboardBroadcaster
{
    public async Task SendAsync(Guid teamId, CancellationToken cancellationToken)
    {
        // A fresh scope: the summary is read with its own DbContext, never the request's.
        using var scope = scopes.CreateScope();
        var summary = await scope.ServiceProvider.GetRequiredService<IDashboardSummaryReader>().GetAsync(teamId, cancellationToken);

        // No reader until BE-11: nothing to send.
        if (summary is not null)
        {
            await hub.Clients.Group(HubGroups.Dashboard(teamId)).DashboardChanged(summary);
        }
    }
}
