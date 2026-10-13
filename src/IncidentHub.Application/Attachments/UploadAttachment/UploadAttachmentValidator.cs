using FluentValidation;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Attachments.UploadAttachment;

public sealed class UploadAttachmentValidator : AbstractValidator<UploadAttachmentCommand>
{
    public UploadAttachmentValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().OverridePropertyName("file").WithMessage("File name is required.");

        RuleFor(x => x)
            .Must(x => AttachmentRules.IsAllowed(x.FileName, x.ContentType))
            .When(x => !string.IsNullOrWhiteSpace(x.FileName))
            .OverridePropertyName("file")
            .WithMessage("File type is not allowed. Use an image (png, jpg, gif, webp), .txt, .log or .json whose content type matches its extension.");
    }
}
