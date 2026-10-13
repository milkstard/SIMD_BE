using IncidentHub.Application.Incidents.Dtos;

namespace IncidentHub.Application.Realtime;

/// <summary>Reads what a broadcast carries, after the commit. Summaries hold the public comment count: they reach Reporters too.</summary>
public interface IIncidentBroadcastReader
{
    Task<BroadcastIncident?> GetIncidentAsync(Guid incidentId, CancellationToken cancellationToken);

    Task<CommentDto?> GetCommentAsync(Guid commentId, CancellationToken cancellationToken);
}

public sealed record BroadcastIncident(IncidentSummaryDto Summary, Guid OwningTeamId);
