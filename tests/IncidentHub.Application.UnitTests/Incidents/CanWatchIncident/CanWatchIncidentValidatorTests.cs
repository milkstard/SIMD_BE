using FluentValidation.TestHelper;
using IncidentHub.Application.Incidents.CanWatchIncident;

namespace IncidentHub.Application.UnitTests.Incidents.CanWatchIncident;

public sealed class CanWatchIncidentValidatorTests
{
    private readonly CanWatchIncidentValidator _validator = new();

    [Fact]
    public void Validate_ValidQuery_NoErrors()
    {
        var result = _validator.TestValidate(new CanWatchIncidentQuery(Guid.NewGuid(), Guid.NewGuid()));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyIncidentId_HasIncidentIdError()
    {
        var result = _validator.TestValidate(new CanWatchIncidentQuery(Guid.Empty, Guid.NewGuid()));

        result.ShouldHaveValidationErrorFor(x => x.IncidentId);
    }

    [Fact]
    public void Validate_EmptyUserId_HasUserIdError()
    {
        var result = _validator.TestValidate(new CanWatchIncidentQuery(Guid.NewGuid(), Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }
}
