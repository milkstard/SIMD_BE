using FluentValidation.TestHelper;
using IncidentHub.Application.Incidents.TransitionIncident;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.UnitTests.Incidents.TransitionIncident;

public sealed class TransitionIncidentValidatorTests
{
    private readonly TransitionIncidentValidator _validator = new();

    private static TransitionIncidentCommand Valid() =>
        new(Guid.NewGuid(), IncidentStatus.Triaged, null, null, null, [1, 2, 3]);

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_UndefinedTo_HasToError()
    {
        _validator.TestValidate(Valid() with { To = (IncidentStatus)(-1) }).ShouldHaveValidationErrorFor(x => x.To);
    }

    [Fact]
    public void Validate_UndefinedCloseReason_HasCloseReasonError()
    {
        _validator.TestValidate(Valid() with { CloseReason = (CloseReason)(-1) }).ShouldHaveValidationErrorFor(x => x.CloseReason);
    }

    [Fact]
    public void Validate_NoteTooLong_HasNoteError()
    {
        var note = new string('x', Incident.NoteMaxLength + 1);

        _validator.TestValidate(Valid() with { Note = note }).ShouldHaveValidationErrorFor(x => x.Note);
    }

    [Fact]
    public void Validate_NoteAtLimit_NoErrors()
    {
        var note = new string('x', Incident.NoteMaxLength);

        _validator.TestValidate(Valid() with { Note = note }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyDuplicateOfId_HasDuplicateOfIdError()
    {
        _validator.TestValidate(Valid() with { DuplicateOfId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.DuplicateOfId);
    }
}
