using IncidentHub.Domain.Users;

namespace IncidentHub.Domain.Incidents;

/// <summary>The status transition table and who may use each row. Single source of truth for <c>Incident.TransitionTo</c>.</summary>
public static class IncidentWorkflow
{
    private static readonly IReadOnlyDictionary<IncidentStatus, (IncidentStatus To, Who Who)[]> Table =
        new Dictionary<IncidentStatus, (IncidentStatus To, Who Who)[]>
        {
            [IncidentStatus.New] =
            [
                (IncidentStatus.Triaged, Who.Responder),
                (IncidentStatus.Closed, Who.Responder),
            ],
            [IncidentStatus.Triaged] =
            [
                (IncidentStatus.InProgress, Who.Responder),
                (IncidentStatus.OnHold, Who.Responder),
            ],
            [IncidentStatus.InProgress] =
            [
                (IncidentStatus.Resolved, Who.Responder | Who.Assignee),
                (IncidentStatus.OnHold, Who.Responder | Who.Assignee),
            ],
            [IncidentStatus.OnHold] =
            [
                (IncidentStatus.InProgress, Who.Responder | Who.Assignee),
            ],
            [IncidentStatus.Resolved] =
            [
                (IncidentStatus.Closed, Who.Responder | Who.Reporter),
                (IncidentStatus.Reopened, Who.Responder | Who.Reporter),
            ],
            [IncidentStatus.Reopened] =
            [
                (IncidentStatus.InProgress, Who.Responder),
            ],
            [IncidentStatus.Closed] = [],
        };

    [Flags]
    private enum Who
    {
        Responder = 1,
        Assignee = 2,
        Reporter = 4,
    }

    /// <summary>True when the table has a row for <paramref name="from"/> to <paramref name="to"/>, whoever the actor is.</summary>
    public static bool IsDefined(IncidentStatus from, IncidentStatus to) =>
        Table.TryGetValue(from, out var rows) && rows.Any(r => r.To == to);

    /// <summary>True when the move exists and <paramref name="actor"/> holds a role or relation the row allows.</summary>
    public static bool IsAllowed(IncidentStatus from, IncidentStatus to, Actor actor, Incident incident)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(incident);

        if (actor.IsSystem || !Table.TryGetValue(from, out var rows))
        {
            return false;
        }

        foreach (var (target, who) in rows)
        {
            if (target == to)
            {
                return Satisfies(who, actor, incident);
            }
        }

        return false;
    }

    /// <summary>The statuses <paramref name="actor"/> may move <paramref name="incident"/> to right now, in table order.</summary>
    public static IReadOnlyList<IncidentStatus> AllowedTransitions(Incident incident, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.IsSystem)
        {
            return [];
        }

        return Table[incident.Status]
            .Where(r => Satisfies(r.Who, actor, incident))
            .Select(r => r.To)
            .ToList();
    }

    private static bool Satisfies(Who who, Actor actor, Incident incident) =>
        (who.HasFlag(Who.Responder) && IsResponder(actor))
        || (who.HasFlag(Who.Assignee) && incident.AssigneeId == actor.UserId)
        || (who.HasFlag(Who.Reporter) && incident.ReporterId == actor.UserId);

    // TeamLead and Admin act as Responder in the workflow (decided for spec 04_01). Team membership is an Api check.
    private static bool IsResponder(Actor actor) =>
        actor.IsInRole(UserRole.Responder) || actor.IsInRole(UserRole.TeamLead) || actor.IsInRole(UserRole.Admin);
}
