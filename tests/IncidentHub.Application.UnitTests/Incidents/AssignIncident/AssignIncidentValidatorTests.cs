using FluentValidation.TestHelper;
using IncidentHub.Application.Incidents.AssignIncident;

namespace IncidentHub.Application.UnitTests.Incidents.AssignIncident;

public sealed class AssignIncidentValidatorTests
{
    private readonly AssignIncidentValidator _validator = new();

    private static AssignIncidentCommand Command(Guid? assigneeId, Guid? teamId) =>
        new(Guid.NewGuid(), assigneeId, teamId, [1, 2, 3]);

    [Fact]
    public void Validate_BothNull_NoErrors()
    {
        _validator.TestValidate(Command(null, null)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_AssigneeAndTeam_NoErrors()
    {
        _validator.TestValidate(Command(Guid.NewGuid(), Guid.NewGuid())).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyGuidAssignee_HasError()
    {
        _validator.TestValidate(Command(Guid.Empty, null)).ShouldHaveValidationErrorFor(x => x.AssigneeId);
    }

    [Fact]
    public void Validate_EmptyGuidTeam_HasError()
    {
        _validator.TestValidate(Command(null, Guid.Empty)).ShouldHaveValidationErrorFor(x => x.TeamId);
    }
}
