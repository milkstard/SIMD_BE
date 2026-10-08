using FluentValidation.TestHelper;
using IncidentHub.Application.Teams.ListTeams;

namespace IncidentHub.Application.UnitTests.Teams;

public sealed class ListTeamsValidatorTests
{
    private readonly ListTeamsValidator _validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_LimitOutOfRange_Fails(int limit) =>
        _validator.TestValidate(new ListTeamsQuery(null, limit)).ShouldHaveValidationErrorFor(x => x.Limit);

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(100)]
    public void Validate_LimitInRange_Passes(int limit) =>
        _validator.TestValidate(new ListTeamsQuery(null, limit)).ShouldNotHaveAnyValidationErrors();
}
