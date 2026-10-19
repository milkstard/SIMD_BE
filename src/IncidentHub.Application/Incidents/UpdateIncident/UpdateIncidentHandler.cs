using FluentValidation;
using FluentValidation.Results;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Incidents.UpdateIncident;

public sealed class UpdateIncidentHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAppTeamReader teams,
    IHtmlSanitizer sanitizer,
    ISlaPolicyProvider slaPolicies,
    TimeProvider time,
    ILogger<UpdateIncidentHandler> logger) : IRequestHandler<UpdateIncidentCommand, IncidentDetailDto>
{
    public async Task<IncidentDetailDto> Handle(UpdateIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await IncidentWriteGuard.LoadForWriteAsync(
            db, teams, currentUser, request.IncidentId, request.ExpectedRowVersion, cancellationToken);

        var actor = currentUser.ToActor();

        // Closed (409) and role (403) win over any 422 that needs a lookup.
        if (incident.Status == IncidentStatus.Closed)
        {
            throw new IncidentClosedException();
        }

        if (!IncidentPermissions.CanEdit(incident.Status, incident.ReporterId, actor)
            || (request.HasResponderField && !IncidentPermissions.IsResponder(actor)))
        {
            throw new ForbiddenException("You are not allowed to make this change to the incident.");
        }

        var effectiveApplicationId = request.ApplicationId.HasValue ? request.ApplicationId.Value!.Value : incident.ApplicationId;
        var effectiveEnvironment = request.Environment.HasValue ? request.Environment.Value!.Value : incident.Environment;
        if (request.ApplicationId.HasValue || request.Environment.HasValue)
        {
            await EnsureApplicationAcceptsAsync(incident, effectiveApplicationId, effectiveEnvironment, cancellationToken);
        }

        var title = request.Title.HasValue ? request.Title.Value!.Trim() : null;
        string? description = null;
        if (request.Description.HasValue)
        {
            description = sanitizer.Sanitize(request.Description.Value!);
            if (string.IsNullOrWhiteSpace(description))
            {
                throw Invalid("description", "Description is empty after removing unsafe content.");
            }
        }

        string? steps = null;
        if (request.StepsToReproduce.HasValue && request.StepsToReproduce.Value is { } rawSteps)
        {
            steps = sanitizer.Sanitize(rawSteps);
            steps = string.IsNullOrWhiteSpace(steps) ? null : steps;
        }

        await EnsureNoOpenDuplicateAsync(incident, effectiveApplicationId, title ?? incident.Title, cancellationToken);

        var now = time.GetUtcNow();
        var changes = new IncidentChanges
        {
            Title = title is null ? default : new Optional<string>(true, title),
            Description = description is null ? default : new Optional<string>(true, description),
            StepsToReproduce = request.StepsToReproduce.HasValue ? new Optional<string?>(true, steps) : default,
            Priority = request.Priority,
            ApplicationId = request.ApplicationId.HasValue ? new Optional<Guid>(true, effectiveApplicationId) : default,
            Environment = request.Environment.HasValue ? new Optional<AppEnvironment>(true, effectiveEnvironment) : default,
        };

        incident.Update(changes, actor, now);

        // A combined edit raises two IncidentUpdated events (fields, then severity); both outbox rows go out in the same save.
        if (request.Severity.HasValue)
        {
            var severity = request.Severity.Value!.Value;
            var policy = await slaPolicies.GetAsync(severity, cancellationToken);
            var due = SlaClock.DueTimes(policy, now);
            incident.ChangeSeverity(severity, request.Reason!, due.AckDueAt, due.ResolveDueAt, due.WarnAt, actor, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Incident {IncidentNumber} updated: {Fields}",
            new IncidentNumber(incident.Number),
            string.Join(',', request.SentFields()));

        return await IncidentProjections.LoadDetailAsync(db, incident.Id, actor, cancellationToken);
    }

    private async Task EnsureApplicationAcceptsAsync(
        Incident incident,
        Guid applicationId,
        AppEnvironment environment,
        CancellationToken cancellationToken)
    {
        var app = await db.Applications
            .AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Select(a => new { a.IsActive, a.Environments })
            .FirstOrDefaultAsync(cancellationToken);

        var moved = applicationId != incident.ApplicationId;
        if (moved && (app is null || !app.IsActive || !await teams.IsMemberAsync(currentUser.UserId, applicationId, cancellationToken)))
        {
            throw Invalid("applicationId", IncidentMessages.ApplicationUnavailable);
        }

        if (app is null || !app.Environments.Contains(environment))
        {
            throw Invalid("environment", IncidentMessages.EnvironmentNotOffered);
        }
    }

    private async Task EnsureNoOpenDuplicateAsync(
        Incident incident,
        Guid applicationId,
        string title,
        CancellationToken cancellationToken)
    {
        var fingerprint = IncidentFingerprint.Compute(applicationId, title);
        if (fingerprint == incident.Fingerprint)
        {
            return;
        }

        var existingNumber = await db.Incidents
            .AsNoTracking()
            .Where(i => i.Fingerprint == fingerprint && i.Status != IncidentStatus.Closed && i.Id != incident.Id)
            .Select(i => (int?)i.Number)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingNumber is { } number)
        {
            throw new DuplicateIncidentException(new IncidentNumber(number).ToString());
        }
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
