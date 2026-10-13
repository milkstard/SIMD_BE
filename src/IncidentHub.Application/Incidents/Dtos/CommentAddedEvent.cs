namespace IncidentHub.Application.Incidents.Dtos;

/// <summary>SignalR <c>CommentAdded</c> payload (API-CONTRACT §7).</summary>
public sealed record CommentAddedEvent(Guid IncidentId, CommentDto Comment);
