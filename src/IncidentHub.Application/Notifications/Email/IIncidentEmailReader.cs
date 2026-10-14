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
    Guid OwningTeamId);

public sealed record EmailRecipient(string Address, string DisplayName);

/// <summary>Small read-only projections for the email handlers. Each returns null when the row no longer exists.</summary>
public interface IIncidentEmailReader
{
    Task<IncidentEmailFacts?> GetIncidentAsync(Guid incidentId, CancellationToken cancellationToken);

    Task<EmailRecipient?> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<EmailRecipient?> GetTeamAsync(Guid teamId, CancellationToken cancellationToken);
}
