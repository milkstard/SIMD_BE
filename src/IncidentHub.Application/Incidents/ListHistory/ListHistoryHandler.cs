using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Incidents.ListHistory;

public sealed class ListHistoryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAppTeamReader teams) : IRequestHandler<ListHistoryQuery, Paged<HistoryEntryDto>>
{
    public async Task<Paged<HistoryEntryDto>> Handle(ListHistoryQuery request, CancellationToken cancellationToken)
    {
        var cursor = TimeKeysetCursor.DecodeOrThrow(request.Cursor);
        await IncidentWriteGuard.EnsureVisibleAsync(db, teams, currentUser, request.IncidentId, cancellationToken);

        // The existence of an internal comment is itself internal, so its history row is filtered out for non-responders.
        var includeInternal = IncidentPermissions.IsResponder(currentUser.ToActor());
        var history = db.IncidentHistory.AsNoTracking()
            .Where(h => h.IncidentId == request.IncidentId
                && (includeInternal || h.Field != Incident.InternalCommentHistoryField));

        var total = request.IncludeTotal ? await history.CountAsync(cancellationToken) : (int?)null;

        // Order, keyset filter and cursor all come from SQL (its uniqueidentifier order differs from .NET's).
        var joined =
            from h in history
            join u in db.Users.AsNoTracking() on h.ActorId equals u.Id into actors
            from actor in actors.DefaultIfEmpty()
            select new { Entry = h, Actor = actor };

        var ordered = joined.OrderByDescending(x => x.Entry.CreatedAt).ThenByDescending(x => x.Entry.Id);
        var filtered = cursor is { } position
            ? ordered.Where(x => x.Entry.CreatedAt < position.At
                || (x.Entry.CreatedAt == position.At && x.Entry.Id.CompareTo(position.Id) < 0))
            : ordered;

        var rows = await filtered
            .Take(request.Limit + 1)
            .Select(x => new
            {
                x.Entry.Id,
                Actor = x.Actor == null ? null : new UserRefDto(x.Actor.Id, x.Actor.DisplayName, x.Actor.Email),
                x.Entry.Field,
                x.Entry.OldValue,
                x.Entry.NewValue,
                x.Entry.Note,
                x.Entry.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        string? next = null;
        if (rows.Count > request.Limit)
        {
            rows.RemoveAt(rows.Count - 1);
            next = TimeKeysetCursor.Encode(rows[^1].CreatedAt, rows[^1].Id);
        }

        var resolved = await HistoryValueResolver.ResolveAsync(
            db,
            rows.Select(r => new HistoryValueResolver.RawEntry(r.Field, r.OldValue, r.NewValue)).ToList(),
            cancellationToken);

        string? Show(string field, string? value) =>
            value is not null && resolved.TryGetValue((field, value), out var text) ? text : value;

        var items = rows
            .Select(r => new HistoryEntryDto(
                r.Id,
                r.Actor,
                HistoryFieldNames.ToDisplayName(r.Field),
                Show(r.Field, r.OldValue),
                Show(r.Field, r.NewValue),
                r.Note,
                r.CreatedAt))
            .ToList();

        return new Paged<HistoryEntryDto>(items, next, total);
    }
}
