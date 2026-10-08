namespace IncidentHub.Application.Incidents.Dtos;

public sealed record CommentDto(
    Guid Id,
    string IncidentNumber,
    string Body,
    bool IsInternal,
    DateTimeOffset CreatedAt);
