using FluentValidation;
using FluentValidation.Results;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Attachments;

/// <summary>Loads the uploads a report or comment wants to link, as tracked entities, or fails with one generic 422.</summary>
internal static class AttachmentLinker
{
    public const string Unavailable = "One or more attachments are unknown, already linked or not available.";

    /// <summary>
    /// Every id must be an unlinked, non-infected upload of <paramref name="callerId"/> (a <c>Pending</c> file is fine). An unknown id,
    /// somebody else's file, a linked file and an infected one all give the same message, so ids cannot be probed.
    /// </summary>
    public static async Task<IReadOnlyList<Attachment>> LoadLinkableAsync(
        IAppDbContext db,
        IReadOnlyList<Guid> ids,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var found = await db.Attachments
            .Where(a => ids.Contains(a.Id)
                && a.UploadedById == callerId
                && a.IncidentId == null
                && a.ScanStatus != ScanStatus.Infected)
            .ToListAsync(cancellationToken);

        if (found.Count != ids.Count)
        {
            throw Unavailable422();
        }

        // Keep the order the caller sent.
        var byId = found.ToDictionary(a => a.Id);
        return ids.Select(id => byId[id]).ToList();
    }

    /// <summary>The 422 for a link that lost a race: the other request linked the file first.</summary>
    public static ValidationException Unavailable422() => new([new ValidationFailure("attachmentIds", Unavailable)]);
}
