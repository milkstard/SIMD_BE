using FluentValidation;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Attachments;

internal static class AttachmentIdRules
{
    /// <summary>At most <see cref="Attachment.MaxPerAction"/> ids, none empty, none repeated. Errors land on <c>attachmentIds</c>.</summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>> MustBeLinkableIds<T>(this IRuleBuilder<T, IReadOnlyList<Guid>> rule) =>
        rule
            .Must(ids => ids.Count <= Attachment.MaxPerAction)
            .WithMessage($"At most {Attachment.MaxPerAction} attachments can be linked.")
            .Must(ids => ids.All(id => id != Guid.Empty))
            .WithMessage("Attachment ids must not be empty.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Attachment ids must be distinct.");
}
