using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;

namespace IncidentHub.Application.Incidents;

/// <summary>Caller-relative flags of an incident. Plain-value inputs so they run on an aggregate and on a projected row alike.</summary>
internal static class IncidentPermissions
{
    public static IReadOnlyList<IncidentStatus> AllowedTransitions(
        IncidentStatus status,
        Guid reporterId,
        Guid? assigneeId,
        Actor actor) =>
        IncidentWorkflow.AllowedTransitions(status, reporterId, assigneeId, actor);

    /// <summary>Responder-level roles, or the reporter, while the incident is open.</summary>
    public static bool CanEdit(IncidentStatus status, Guid reporterId, Actor actor) =>
        status != IncidentStatus.Closed && (IsResponder(actor) || reporterId == actor.UserId);

    /// <summary>Responder-level roles while the incident is open.</summary>
    public static bool CanAssign(IncidentStatus status, Actor actor) =>
        status != IncidentStatus.Closed && IsResponder(actor);

    public static bool IsResponder(Actor actor) => IncidentWorkflow.IsResponder(actor);
}
