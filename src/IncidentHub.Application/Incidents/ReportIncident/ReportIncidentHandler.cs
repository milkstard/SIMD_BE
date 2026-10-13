using FluentValidation;
using FluentValidation.Results;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Attachments;
using IncidentHub.Application.Common;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Incidents.ReportIncident;

public sealed class ReportIncidentHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAppTeamReader teams,
    IHtmlSanitizer sanitizer,
    ISlaPolicyProvider slaPolicies,
    TimeProvider time,
    ILogger<ReportIncidentHandler> logger) : IRequestHandler<ReportIncidentCommand, IncidentDetailDto>
{
    public async Task<IncidentDetailDto> Handle(ReportIncidentCommand request, CancellationToken cancellationToken)
    {
        var app = await db.Applications
            .AsNoTracking()
            .Where(a => a.Id == request.ApplicationId)
            .Select(a => new { a.Name, a.Code, a.IsActive, a.Environments })
            .FirstOrDefaultAsync(cancellationToken);

        if (app is null
            || !app.IsActive
            || !await teams.IsMemberAsync(currentUser.UserId, request.ApplicationId, cancellationToken))
        {
            throw Invalid(nameof(request.ApplicationId), IncidentMessages.ApplicationUnavailable);
        }

        if (!app.Environments.Contains(request.Environment))
        {
            throw Invalid(nameof(request.Environment), IncidentMessages.EnvironmentNotOffered);
        }

        var title = request.Title.Trim();
        var description = sanitizer.Sanitize(request.Description);
        if (string.IsNullOrWhiteSpace(description))
        {
            throw Invalid(nameof(request.Description), "Description is empty after removing unsafe content.");
        }

        var steps = request.StepsToReproduce is null ? null : sanitizer.Sanitize(request.StepsToReproduce);

        var fingerprint = IncidentFingerprint.Compute(request.ApplicationId, title);
        var existingNumber = await db.Incidents
            .AsNoTracking()
            .Where(i => i.Fingerprint == fingerprint && i.Status != IncidentStatus.Closed)
            .Select(i => (int?)i.Number)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingNumber is { } number)
        {
            throw new DuplicateIncidentException(new IncidentNumber(number).ToString());
        }

        var attachments = await AttachmentLinker.LoadLinkableAsync(db, request.AttachmentIds, currentUser.UserId, cancellationToken);

        var actor = currentUser.ToActor();
        var incident = Incident.Report(
            request.ApplicationId,
            request.Environment,
            request.Severity,
            title,
            description,
            string.IsNullOrWhiteSpace(steps) ? null : steps,
            IncidentSource.Manual,
            actor,
            time.GetUtcNow(),
            attachments: attachments);

        var policy = await slaPolicies.GetAsync(request.Severity, cancellationToken);
        var due = SlaClock.DueTimes(policy, incident.CreatedAt);
        incident.SetSlaDueTimes(due.AckDueAt, due.ResolveDueAt);

        db.Incidents.Add(incident);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) when (attachments.Count > 0)
        {
            // The link is UPDATE ... WHERE IncidentId IS NULL: a parallel request linked one of the files first, and this save rolled back.
            throw AttachmentLinker.Unavailable422();
        }

        var uploader = new UserRefDto(currentUser.UserId, currentUser.DisplayName, currentUser.Email);

        // Built from values already in memory: a failing read after the commit would turn a stored incident into a 500 and a confusing duplicate 409 on retry.
        var detail = IncidentDetailMapper.ToDetail(
            incident,
            actor,
            new ApplicationRefDto(request.ApplicationId, app.Name, app.Code),
            uploader,
            attachments.Select(a => AttachmentDto.From(a, uploader)).ToList());
        logger.LogInformation(
            "Incident {IncidentNumber} reported for application {ApplicationId} with severity {Severity}",
            detail.Number,
            request.ApplicationId,
            request.Severity);
        return detail;
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
