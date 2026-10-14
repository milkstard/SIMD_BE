using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Realtime;

/// <summary>
/// Turns the domain events of one committed save into the messages to push and where (API-CONTRACT §7). Pure orchestration: it reads
/// through <see cref="IIncidentBroadcastReader"/> and sends nothing. Updates of one incident collapse into one message because the
/// summary is read after the commit anyway.
/// </summary>
public sealed class IncidentBroadcaster(
    IIncidentBroadcastReader reader, IAppStatusReader appStatuses, ILogger<IncidentBroadcaster> logger)
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

        // The status block is best effort: a failure here must not drop the broadcasts of the save.
        try
        {
            foreach (var applicationId in await TouchedApplicationsAsync(events, reported, LoadAsync))
            {
                if (await appStatuses.RefreshAsync(applicationId, cancellationToken) is not { } refresh)
                {
                    continue;
                }

                // Pushed on a real change, and on the first derivation: with nothing cached (TTL expired) a recovery
                // must not be missed, so an unchanged status can then be pushed once more.
                if (refresh.Previous != refresh.Current.Status)
                {
                    messages.Add(new AppStatusChangedMessage(refresh.Current, [new(BroadcastAudience.Dashboard, refresh.OwningTeamId)]));
                    AddTeam(teams, refresh.OwningTeamId);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "App status refresh failed; the other broadcasts of this save are still sent");
        }

        foreach (var added in comments)
        {
            if (await LoadAsync(added.IncidentId) is not { } incident
                || await reader.GetCommentAsync(added.CommentId, cancellationToken) is not { } comment)
            {
                continue;
            }

            // An internal comment never goes to the incident group: Reporters can be in it.
            // Either flag makes it internal: the stored row wins over the event if they ever disagree.
            var target = added.IsInternal || comment.IsInternal
                ? new BroadcastTarget(BroadcastAudience.Responders, incident.OwningTeamId)
                : new BroadcastTarget(BroadcastAudience.Incident, added.IncidentId);
            messages.Add(new CommentAddedMessage(new CommentAddedEvent(added.IncidentId, comment), [target]));
            AddTeam(teams, incident.OwningTeamId);
        }

        return messages.Count == 0 ? BroadcastPlan.Empty : new BroadcastPlan(messages, teams);
    }

    /// <summary>Applications whose derived status an event may have changed: reports, transitions, and edits of severity, environment or application (old one included).</summary>
    private static async Task<IReadOnlyList<Guid>> TouchedApplicationsAsync(
        IReadOnlyList<IDomainEvent> events,
        List<Guid> reported,
        Func<Guid, Task<BroadcastIncident?>> load)
    {
        var applications = new List<Guid>();

        void Add(Guid id)
        {
            if (!applications.Contains(id))
            {
                applications.Add(id);
            }
        }

        foreach (var id in reported)
        {
            if (await load(id) is { } incident)
            {
                Add(incident.Summary.Application.Id);
            }
        }

        foreach (var transitioned in events.OfType<IncidentTransitioned>().Select(t => t.IncidentId).Distinct())
        {
            if (!reported.Contains(transitioned) && await load(transitioned) is { } incident)
            {
                Add(incident.Summary.Application.Id);
            }
        }

        foreach (var edit in events.OfType<IncidentUpdated>().Where(u => u.ChangedFields.Any(StatusFields.Contains)))
        {
            if (!reported.Contains(edit.IncidentId) && await load(edit.IncidentId) is { } incident)
            {
                Add(incident.Summary.Application.Id);
            }

            if (edit.PreviousApplicationId is { } previous)
            {
                Add(previous);
            }
        }

        return applications;
    }

    private static readonly HashSet<string> StatusFields = ["Severity", "Environment", "ApplicationId"];

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
