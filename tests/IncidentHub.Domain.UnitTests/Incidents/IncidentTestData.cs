using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;

namespace IncidentHub.Domain.UnitTests.Incidents;

public enum ActorKind
{
    Responder,
    TeamLead,
    Admin,
    Assignee,
    Reporter,
    Stranger,
    System,
}

/// <summary>An incident in a given status plus the actors that relate to it differently (reporter, assignee, others).</summary>
public sealed record IncidentFixture(Incident Incident, Guid ReporterId, Guid AssigneeId)
{
    public Actor ActorOf(ActorKind kind) => kind switch
    {
        ActorKind.Responder => IncidentTestData.ActorWithRole(UserRole.Responder),
        ActorKind.TeamLead => IncidentTestData.ActorWithRole(UserRole.TeamLead),
        ActorKind.Admin => IncidentTestData.ActorWithRole(UserRole.Admin),
        ActorKind.Assignee => IncidentTestData.ActorWithRole(UserRole.Reporter, AssigneeId),
        ActorKind.Reporter => IncidentTestData.ActorWithRole(UserRole.Reporter, ReporterId),
        ActorKind.Stranger => IncidentTestData.ActorWithRole(UserRole.Reporter),
        ActorKind.System => Actor.System,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

public static class IncidentTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    public static readonly Guid ApplicationId = Guid.Parse("b3f10000-0000-0000-0000-000000000001");

    public static Actor ActorWithRole(UserRole role, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), new HashSet<UserRole> { role });

    public static Incident Report(Actor? reporter = null, string title = "Checkout fails with 500") =>
        Incident.Report(
            ApplicationId,
            AppEnvironment.Production,
            Severity.High,
            title,
            "<p>Users see an error page after paying.</p>",
            "1. Pay",
            IncidentSource.Manual,
            reporter ?? ActorWithRole(UserRole.Reporter),
            Now);

    /// <summary>Builds an incident in <paramref name="status"/> by walking the legal path, with an assignee set. Events are cleared.</summary>
    public static IncidentFixture Build(IncidentStatus status)
    {
        var reporterId = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        var responder = ActorWithRole(UserRole.Responder);
        var incident = Report(ActorWithRole(UserRole.Reporter, reporterId));
        incident.Assign(assigneeId, null, responder, Now);

        foreach (var step in PathTo(status))
        {
            var (note, reason, duplicateOf) = InputsFor(incident.Status, step);
            incident.TransitionTo(step, responder, note, Now, reason, duplicateOf);
        }

        incident.ClearDomainEvents();
        return new IncidentFixture(incident, reporterId, assigneeId);
    }

    /// <summary>Valid optional inputs for a transition (note always present; close reason when closing).</summary>
    public static (string? Note, CloseReason? Reason, Guid? DuplicateOfId) InputsFor(IncidentStatus from, IncidentStatus to) =>
        to == IncidentStatus.Closed
            ? ("done", from == IncidentStatus.New ? CloseReason.Invalid : CloseReason.Fixed, null)
            : ("done", null, null);

    private static IncidentStatus[] PathTo(IncidentStatus status) => status switch
    {
        IncidentStatus.New => [],
        IncidentStatus.Triaged => [IncidentStatus.Triaged],
        IncidentStatus.InProgress => [IncidentStatus.Triaged, IncidentStatus.InProgress],
        IncidentStatus.OnHold => [IncidentStatus.Triaged, IncidentStatus.InProgress, IncidentStatus.OnHold],
        IncidentStatus.Resolved => [IncidentStatus.Triaged, IncidentStatus.InProgress, IncidentStatus.Resolved],
        IncidentStatus.Reopened => [IncidentStatus.Triaged, IncidentStatus.InProgress, IncidentStatus.Resolved, IncidentStatus.Reopened],
        IncidentStatus.Closed => [IncidentStatus.Triaged, IncidentStatus.InProgress, IncidentStatus.Resolved, IncidentStatus.Closed],
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
