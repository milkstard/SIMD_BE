using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Notifications.Email;

public sealed class IncidentEmailReader(IAppDbContext db) : IIncidentEmailReader
{
    public async Task<IncidentEmailFacts?> GetIncidentAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        var row = await (
                from incident in db.Incidents.AsNoTracking()
                join app in db.Applications.AsNoTracking() on incident.ApplicationId equals app.Id
                where incident.Id == incidentId
                select new
                {
                    incident.Number,
                    incident.Title,
                    ApplicationName = app.Name,
                    incident.Environment,
                    incident.Severity,
                    incident.Status,
                    incident.ReporterId,
                    app.OwningTeamId,
                })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new IncidentEmailFacts(
                new IncidentNumber(row.Number).ToString(),
                row.Title,
                row.ApplicationName,
                row.Environment,
                row.Severity,
                row.Status,
                row.ReporterId,
                row.OwningTeamId);
    }

    public Task<EmailRecipient?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new EmailRecipient(u.Email, u.DisplayName))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<EmailRecipient?> GetTeamAsync(Guid teamId, CancellationToken cancellationToken) =>
        db.Teams.AsNoTracking()
            .Where(t => t.Id == teamId)
            .Select(t => new EmailRecipient(t.Email, t.Name))
            .FirstOrDefaultAsync(cancellationToken);
}
