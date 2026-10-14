using IncidentHub.Application.Dashboard.Dtos;
using IncidentHub.Application.Incidents.Dtos;

namespace IncidentHub.Application.Realtime;

/// <summary>Application-side name of a SignalR group; the Api maps it to the group string.</summary>
public enum BroadcastAudience
{
    Dashboard,
    Team,
    Responders,
    Incident,
}

public readonly record struct BroadcastTarget(BroadcastAudience Audience, Guid Id);

public abstract record BroadcastMessage(IReadOnlyList<BroadcastTarget> Targets);

public sealed record IncidentCreatedMessage(IncidentSummaryDto Incident, IReadOnlyList<BroadcastTarget> Targets) : BroadcastMessage(Targets);

public sealed record IncidentUpdatedMessage(IncidentSummaryDto Incident, IReadOnlyList<BroadcastTarget> Targets) : BroadcastMessage(Targets);

public sealed record CommentAddedMessage(CommentAddedEvent Event, IReadOnlyList<BroadcastTarget> Targets) : BroadcastMessage(Targets);

public sealed record AppStatusChangedMessage(AppStatusItemDto Item, IReadOnlyList<BroadcastTarget> Targets) : BroadcastMessage(Targets);

/// <param name="DashboardTeamIds">Teams whose dashboard changed; each one gets a (throttled) <c>DashboardChanged</c>.</param>
public sealed record BroadcastPlan(IReadOnlyList<BroadcastMessage> Messages, IReadOnlyList<Guid> DashboardTeamIds)
{
    public static BroadcastPlan Empty { get; } = new([], []);
}
