using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Notifications.Email;

/// <summary>The incident data an email shows, read when the mail is built (not when the event happened).</summary>
public sealed record IncidentEmailFacts(
    string Number,
    string Title,
    string ApplicationName,
    AppEnvironment Environment,
    Severity Severity,
    IncidentStatus Status,
    Guid ReporterId,
    Guid OwningTeamId,
    Guid? AssigneeId,
    DateTimeOffset? ResolveDueAt,
    DateTimeOffset? SlaWarnedAt,
    Guid? EscalationUserId,
    int? EscalateAtPercent);

/// <param name="NotificationPrefs">The user's stored preference JSON; <c>null</c> for a team (and for tests that do not care).</param>
public sealed record EmailRecipient(string Address, string DisplayName, string? NotificationPrefs = null);

/// <summary>Small read-only projections for the email handlers. Each returns null when the row no longer exists.</summary>
public interface IIncidentEmailReader
{
    Task<IncidentEmailFacts?> GetIncidentAsync(Guid incidentId, CancellationToken cancellationToken);

    Task<EmailRecipient?> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<EmailRecipient?> GetTeamAsync(Guid teamId, CancellationToken cancellationToken);

    /// <summary>The team's Teams or Slack webhook URL, or null when the team has none (or no longer exists).</summary>
    Task<string?> GetTeamChannelUrlAsync(Guid teamId, CancellationToken cancellationToken);
}
