using IncidentHub.Api.Auth;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace IncidentHub.Api.Hubs;

/// <summary>Server → client contract. Always use the typed hub; never string method names.</summary>
public interface IIncidentClient
{
    Task CommentAdded(CommentDto comment);
}

public static class HubGroups
{
    /// <summary>Everyone on the application's team (public events).</summary>
    public static string App(Guid applicationId) => $"app:{applicationId:N}";

    /// <summary>Responder-level team members only (internal comments).</summary>
    public static string Responders(Guid applicationId) => $"app:{applicationId:N}:responders";
}

[Authorize(Policy = Policies.CanViewIncident)]
public sealed class IncidentsHub(IAppTeamReader teamReader) : Hub<IIncidentClient>
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        var userId = user.GetUserId();
        if (userId is null)
        {
            Context.Abort();
            return;
        }

        var roles = user.GetRoles();
        var isResponder = roles.Contains(UserRole.Responder) || roles.Contains(UserRole.TeamLead) || roles.Contains(UserRole.Admin);
        var applicationIds = await teamReader.GetApplicationIdsForUserAsync(userId.Value, Context.ConnectionAborted);

        foreach (var applicationId in applicationIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.App(applicationId), Context.ConnectionAborted);
            if (isResponder)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Responders(applicationId), Context.ConnectionAborted);
            }
        }

        await base.OnConnectedAsync();
    }
}
