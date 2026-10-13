using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;

namespace IncidentHub.Application.Realtime;

/// <summary>
/// Turns the domain events of one committed save into the messages to push and where (API-CONTRACT §7). Pure orchestration: it reads
/// through <see cref="IIncidentBroadcastReader"/> and sends nothing. Updates of one incident collapse into one message because the
/// summary is read after the commit anyway.
/// </summary>
public sealed class IncidentBroadcaster(IIncidentBroadcastReader reader)
{
    public async Task<BroadcastPlan> PlanAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        var reported = events.OfType<IncidentReported>().Select(e => e.IncidentId).Distinct().ToList();
        var updated = events
            .Select(UpdatedIncidentId)
            .OfType<Guid>()
            .Distinct()
            .Where(id => !reported.Contains(id))
            .ToList();
        var comments = events.OfType<CommentAdded>().ToList();

        var messages = new List<BroadcastMessage>();
        var teams = new List<Guid>();
        var incidents = new Dictionary<Guid, BroadcastIncident?>();

        async Task<BroadcastIncident?> LoadAsync(Guid id)
        {
            if (!incidents.TryGetValue(id, out var incident))
            {
                incident = await reader.GetIncidentAsync(id, cancellationToken);
                incidents[id] = incident;
            }

            return incident;
        }

        foreach (var id in reported)
        {
            if (await LoadAsync(id) is { } incident)
            {
                messages.Add(new IncidentCreatedMessage(
                    incident.Summary,
                    [new(BroadcastAudience.Dashboard, incident.OwningTeamId), new(BroadcastAudience.Team, incident.OwningTeamId)]));
                AddTeam(teams, incident.OwningTeamId);
            }
        }

        foreach (var id in updated)
        {
            if (await LoadAsync(id) is { } incident)
            {
                messages.Add(new IncidentUpdatedMessage(
                    incident.Summary,
                    [
                        new(BroadcastAudience.Dashboard, incident.OwningTeamId),
                        new(BroadcastAudience.Incident, id),
                        new(BroadcastAudience.Team, incident.OwningTeamId),
                    ]));
                AddTeam(teams, incident.OwningTeamId);
            }
        }

        foreach (var added in comments)
        {
            if (await LoadAsync(added.IncidentId) is not { } incident
                || await reader.GetCommentAsync(added.CommentId, cancellationToken) is not { } comment)
            {
                continue;
            }

            // An internal comment never goes to the incident group: Reporters can be in it.
            var target = added.IsInternal
                ? new BroadcastTarget(BroadcastAudience.Responders, incident.OwningTeamId)
                : new BroadcastTarget(BroadcastAudience.Incident, added.IncidentId);
            messages.Add(new CommentAddedMessage(new CommentAddedEvent(added.IncidentId, comment), [target]));
            AddTeam(teams, incident.OwningTeamId);
        }

        return messages.Count == 0 ? BroadcastPlan.Empty : new BroadcastPlan(messages, teams);
    }

    private static Guid? UpdatedIncidentId(IDomainEvent e) => e switch
    {
        IncidentTransitioned t => t.IncidentId,
        IncidentAssigned a => a.IncidentId,
        IncidentUpdated u => u.IncidentId,
        _ => null,
    };

    private static void AddTeam(List<Guid> teams, Guid teamId)
    {
        if (!teams.Contains(teamId))
        {
            teams.Add(teamId);
        }
    }
}
