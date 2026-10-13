using FluentValidation;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Incidents.AddComment;

public sealed class AddCommentValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("Body is required.")
            .MaximumLength(Comment.BodyMaxLength);
    }
}
