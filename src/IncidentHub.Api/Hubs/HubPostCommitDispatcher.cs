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

        // Dashboards first: Notify is synchronous and cheap, and must not be lost to a failing send below.
        foreach (var teamId in plan.DashboardTeamIds)
        {
            dashboard.Notify(teamId);
        }

        // One failing send must not drop the rest of the save's broadcasts: collect, then fail once so the caller still logs it.
        List<Exception>? failures = null;
        foreach (var message in plan.Messages)
        {
            try
            {
                await SendAsync(message);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(failures);
        }
    }

    private Task SendAsync(BroadcastMessage message)
    {
        var clients = hub.Clients.Groups(message.Targets.Select(ToGroup).ToList());
        return message switch
        {
            IncidentCreatedMessage created => clients.IncidentCreated(created.Incident),
            IncidentUpdatedMessage updated => clients.IncidentUpdated(updated.Incident),
            CommentAddedMessage comment => clients.CommentAdded(comment.Event),
            AppStatusChangedMessage status => clients.AppStatusChanged(status.Item),
            _ => Task.CompletedTask,
        };
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
