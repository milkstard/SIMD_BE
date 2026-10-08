using FluentValidation;
using FluentValidation.Results;
using IncidentHub.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Apps;

/// <summary>Checks that need data, so they live in handlers rather than validators.</summary>
internal static class AppReferenceChecks
{
    public static async Task EnsureReferencesExistAsync(
        IAppDbContext db,
        Guid owningTeamId,
        Guid? escalationContactId,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();

        if (!await db.Teams.AnyAsync(t => t.Id == owningTeamId, cancellationToken))
        {
            failures.Add(new ValidationFailure("owningTeamId", "Owning team does not exist."));
        }

        if (escalationContactId is { } userId && !await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
        {
            failures.Add(new ValidationFailure("escalationContactId", "Escalation contact does not exist."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    public static async Task<bool> CodeTakenAsync(IAppDbContext db, string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = code.ToUpperInvariant();
        return await db.Applications.AnyAsync(a => a.Code == normalized && a.Id != exceptId, cancellationToken);
    }
}
