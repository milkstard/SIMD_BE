using IncidentHub.Application.Common;

namespace IncidentHub.Application.Incidents.Dtos;

/// <summary>API-CONTRACT §4 <c>HistoryEntry</c>; <c>Actor</c> is null for the system.</summary>
public sealed record HistoryEntryDto(
    Guid Id,
    UserRefDto? Actor,
    string Field,
    string? OldValue,
    string? NewValue,
    string? Note,
    DateTimeOffset At);
