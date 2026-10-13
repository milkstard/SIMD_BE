using IncidentHub.Api.Auth;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Dashboard.Dtos;
using IncidentHub.Application.Incidents.CanWatchIncident;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Application.Notifications.Dtos;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace IncidentHub.Api.Hubs;

/// <summary>Server → client contract (API-CONTRACT §7). Always use the typed hub; never string method names.</summary>
public interface IIncidentClient
{
    Task IncidentCreated(IncidentSummaryDto incident);

    Task IncidentUpdated(IncidentSummaryDto incident);

    Task CommentAdded(CommentAddedEvent e);

    Task AppStatusChanged(AppStatusItemDto item);

    Task DashboardChanged(DashboardSummaryDto summary);

    Task NotificationReceived(NotificationDto notification);
}

public static class HubGroups
{
    /// <summary>Live dashboard of one team; joined with <c>JoinDashboard</c>.</summary>
    public static string Dashboard(Guid teamId) => $"dashboard:{teamId:N}";

    /// <summary>Every member of the team.</summary>
    public static string Team(Guid teamId) => $"team:{teamId:N}";

    /// <summary>Responder-level members of the team only (internal comments).</summary>
    public static string Responders(Guid teamId) => $"responders:{teamId:N}";

    public static string Incident(Guid incidentId) => $"incident:{incidentId:N}";

    public static string User(Guid userId) => $"user:{userId:N}";
}

/// <summary>Group management only: the hub holds no business logic. Team groups are fixed at connect time (a membership change applies on reconnect).</summary>
[Authorize(Policy = Policies.CanViewIncident)]
public sealed class IncidentsHub(IAppTeamReader teams, ISender sender) : Hub<IIncidentClient>
{
    public const string IncidentNotFound = "Incident not found.";

    public override async Task OnConnectedAsync()
    {
        var principal = Context.User!;
        var userId = principal.GetUserId();
        if (userId is null)
        {
            Context.Abort();
            return;
        }

        var isResponder = IncidentWorkflow.IsResponder(new Actor(userId.Value, principal.GetRoles()));
        var teamIds = await teams.GetTeamIdsForUserAsync(userId.Value, Context.ConnectionAborted);

        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.User(userId.Value), Context.ConnectionAborted);
        foreach (var teamId in teamIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Team(teamId), Context.ConnectionAborted);
            if (isResponder)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Responders(teamId), Context.ConnectionAborted);
            }
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinDashboard()
    {
        foreach (var teamId in await OwnTeamIdsAsync())
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Dashboard(teamId), Context.ConnectionAborted);
        }
    }

    public async Task LeaveDashboard()
    {
        foreach (var teamId in await OwnTeamIdsAsync())
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Dashboard(teamId), Context.ConnectionAborted);
        }
    }

    public async Task WatchIncident(Guid incidentId)
    {
        var userId = Context.User!.GetUserId();

        // An unknown, malformed or inaccessible incident gets the same error.
        if (userId is null
            || incidentId == Guid.Empty
            || !await sender.Send(new CanWatchIncidentQuery(incidentId, userId.Value), Context.ConnectionAborted))
        {
            throw new HubException(IncidentNotFound);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Incident(incidentId), Context.ConnectionAborted);
    }

    public Task UnwatchIncident(Guid incidentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Incident(incidentId), Context.ConnectionAborted);

    private async Task<IReadOnlyList<Guid>> OwnTeamIdsAsync() =>
        Context.User!.GetUserId() is { } userId
            ? await teams.GetTeamIdsForUserAsync(userId, Context.ConnectionAborted)
            : [];
}
