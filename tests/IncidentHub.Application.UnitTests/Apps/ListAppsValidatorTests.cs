using FluentValidation.TestHelper;
using IncidentHub.Application.Apps.ListApps;

namespace IncidentHub.Application.UnitTests.Apps;

public sealed class ListAppsValidatorTests
{
    private readonly ListAppsValidator _validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_LimitOutOfRange_Fails(int limit) =>
        _validator.TestValidate(new ListAppsQuery(null, limit)).ShouldHaveValidationErrorFor(x => x.Limit);

    [Fact]
    public void Validate_DefaultLimit_Passes() =>
        _validator.TestValidate(new ListAppsQuery(null)).ShouldNotHaveAnyValidationErrors();
}
