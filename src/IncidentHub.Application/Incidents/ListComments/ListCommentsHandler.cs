using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Incidents.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Incidents.ListComments;

public sealed class ListCommentsHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAppTeamReader teams) : IRequestHandler<ListCommentsQuery, Paged<CommentDto>>
{
    public async Task<Paged<CommentDto>> Handle(ListCommentsQuery request, CancellationToken cancellationToken)
    {
        var cursor = TimeKeysetCursor.DecodeOrThrow(request.Cursor);
        await IncidentWriteGuard.EnsureVisibleAsync(db, teams, currentUser, request.IncidentId, cancellationToken);

        // Applied in the query so internal rows are never materialised for a non-responder.
        var includeInternal = IncidentPermissions.IsResponder(currentUser.ToActor());
        var comments = db.Comments.AsNoTracking()
            .Where(c => c.IncidentId == request.IncidentId && (includeInternal || !c.IsInternal));

        var total = request.IncludeTotal ? await comments.CountAsync(cancellationToken) : (int?)null;

        // Join first, then order and page the joined rows in SQL: uniqueidentifier ordering in SQL Server differs from
        // Guid.CompareTo in .NET, so the order, the keyset filter and the cursor must all come from the database.
        var joined =
            from c in comments
            join u in db.Users.AsNoTracking() on c.AuthorId equals u.Id
            select new { Comment = c, Author = u };

        var ordered = joined.OrderBy(x => x.Comment.CreatedAt).ThenBy(x => x.Comment.Id);
        var filtered = cursor is { } position
            ? ordered.Where(x => x.Comment.CreatedAt > position.At
                || (x.Comment.CreatedAt == position.At && x.Comment.Id.CompareTo(position.Id) > 0))
            : ordered;

        var rows = await filtered
            .Take(request.Limit + 1)
            .Select(x => new CommentDto(
                x.Comment.Id,
                x.Comment.IncidentId,
                new UserRefDto(x.Author.Id, x.Author.DisplayName, x.Author.Email),
                x.Comment.Body,
                x.Comment.IsInternal,
                Array.Empty<AttachmentDto>(),
                x.Comment.CreatedAt,
                x.Comment.EditedAt))
            .ToListAsync(cancellationToken);

        string? next = null;
        if (rows.Count > request.Limit)
        {
            rows.RemoveAt(rows.Count - 1);
            next = TimeKeysetCursor.Encode(rows[^1].CreatedAt, rows[^1].Id);
        }

        rows = await IncidentProjections.WithAttachmentsAsync(db, rows, cancellationToken);

        return new Paged<CommentDto>(rows, next, total);
    }
}
