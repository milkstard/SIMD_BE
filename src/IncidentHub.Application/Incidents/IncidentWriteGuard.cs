using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Incidents;

internal static class IncidentWriteGuard
{
    /// <summary>
    /// Loads the incident tracked for a write. An unknown incident and one outside the caller's teams give the same
    /// <see cref="NotFoundException"/>; the membership check runs before the version check so a non-member learns nothing from a 409.
    /// A stale <paramref name="expectedRowVersion"/> is a <see cref="ConflictException"/>; a writer that commits after this load
    /// makes the save fail with <see cref="DbUpdateConcurrencyException"/>, which is also a 409.
    /// </summary>
    public static async Task<Incident> LoadForWriteAsync(
        IAppDbContext db,
        IAppTeamReader teams,
        ICurrentUser currentUser,
        Guid incidentId,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken)
    {
        var incident = await LoadVisibleAsync(db, teams, currentUser, incidentId, cancellationToken);

        if (!incident.RowVersion.AsSpan().SequenceEqual(expectedRowVersion))
        {
            throw new ConflictException($"Incident {new IncidentNumber(incident.Number)} was modified by someone else.");
        }

        return incident;
    }

    /// <summary>Loads the incident tracked for a write that needs no <c>If-Match</c> (a comment). Unknown and not-visible are both 404.</summary>
    public static async Task<Incident> LoadVisibleAsync(
        IAppDbContext db,
        IAppTeamReader teams,
        ICurrentUser currentUser,
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

        if (incident is null
            || !await teams.IsMemberAsync(currentUser.UserId, incident.ApplicationId, cancellationToken))
        {
            throw new NotFoundException(IncidentMessages.IncidentNotFound);
        }

        return incident;
    }

    /// <summary>Read-side visibility check that loads no aggregate. Unknown and not-visible are both 404.</summary>
    public static async Task EnsureVisibleAsync(
        IAppDbContext db,
        IAppTeamReader teams,
        ICurrentUser currentUser,
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        var applicationId = await db.Incidents.AsNoTracking()
            .Where(i => i.Id == incidentId)
            .Select(i => (Guid?)i.ApplicationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (applicationId is null
            || !await teams.IsMemberAsync(currentUser.UserId, applicationId.Value, cancellationToken))
        {
            throw new NotFoundException(IncidentMessages.IncidentNotFound);
        }
    }
}
