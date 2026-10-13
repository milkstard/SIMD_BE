using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Realtime;
using IncidentHub.Domain.Common;
using Microsoft.AspNetCore.SignalR;

namespace IncidentHub.Api.Hubs;

/// <summary>
/// Pushes the events of a committed save to SignalR through the typed hub context. Plans the messages in a fresh scope (own
/// DbContext, so the request's change tracker is untouched). Several groups of one message are sent in a single call, so a connection
/// in two of them receives it once.
/// </summary>
public sealed class HubPostCommitDispatcher(
    IServiceScopeFactory scopes,
    IHubContext<IncidentsHub, IIncidentClient> hub,
    DashboardThrottle dashboard) : IPostCommitDispatcher
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        BroadcastPlan plan;
        using (var scope = scopes.CreateScope())
        {
            plan = await scope.ServiceProvider.GetRequiredService<IncidentBroadcaster>().PlanAsync(events, cancellationToken);
        }

        foreach (var message in plan.Messages)
        {
            var clients = hub.Clients.Groups(message.Targets.Select(ToGroup).ToList());
            switch (message)
            {
                case IncidentCreatedMessage created:
                    await clients.IncidentCreated(created.Incident);
                    break;
                case IncidentUpdatedMessage updated:
                    await clients.IncidentUpdated(updated.Incident);
                    break;
                case CommentAddedMessage comment:
                    await clients.CommentAdded(comment.Event);
                    break;
            }
        }

        foreach (var teamId in plan.DashboardTeamIds)
        {
            dashboard.Notify(teamId);
        }
    }

    private static string ToGroup(BroadcastTarget target) => target.Audience switch
    {
        BroadcastAudience.Dashboard => HubGroups.Dashboard(target.Id),
        BroadcastAudience.Team => HubGroups.Team(target.Id),
        BroadcastAudience.Responders => HubGroups.Responders(target.Id),
        BroadcastAudience.Incident => HubGroups.Incident(target.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}
