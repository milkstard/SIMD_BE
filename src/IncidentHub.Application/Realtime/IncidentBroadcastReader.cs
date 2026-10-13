using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Incidents;
using IncidentHub.Application.Incidents.Dtos;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Realtime;

public sealed class IncidentBroadcastReader(IAppDbContext db) : IIncidentBroadcastReader
{
    public async Task<BroadcastIncident?> GetIncidentAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        var row = await IncidentProjections
            .SummaryRows(db, db.Incidents.Where(i => i.Id == incidentId), includeInternalComments: false)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var teamId = await db.Applications.AsNoTracking()
            .Where(a => a.Id == row.Application.Id)
            .Select(a => (Guid?)a.OwningTeamId)
            .FirstOrDefaultAsync(cancellationToken);

        return teamId is { } owner ? new BroadcastIncident(IncidentProjections.ToSummary(row), owner) : null;
    }

    public async Task<CommentDto?> GetCommentAsync(Guid commentId, CancellationToken cancellationToken)
    {
        var comment = await (
            from c in db.Comments.AsNoTracking()
            where c.Id == commentId
            join u in db.Users.AsNoTracking() on c.AuthorId equals u.Id
            select new CommentDto(
                c.Id,
                c.IncidentId,
                new UserRefDto(u.Id, u.DisplayName, u.Email),
                c.Body,
                c.IsInternal,
                Array.Empty<AttachmentDto>(),
                c.CreatedAt,
                c.EditedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return comment is null
            ? null
            : (await IncidentProjections.WithAttachmentsAsync(db, [comment], cancellationToken))[0];
    }
}
