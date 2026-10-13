using FluentValidation.TestHelper;
using IncidentHub.Application.Incidents.FindSimilarIncidents;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.UnitTests.Incidents.FindSimilarIncidents;

public sealed class FindSimilarIncidentsValidatorTests
{
    private readonly FindSimilarIncidentsValidator _validator = new();

    [Fact]
    public void Validate_ValidQuery_NoErrors()
    {
        var result = _validator.TestValidate(new FindSimilarIncidentsQuery(Guid.NewGuid(), "Checkout fails"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyApplicationId_HasApplicationIdError()
    {
        var result = _validator.TestValidate(new FindSimilarIncidentsQuery(Guid.Empty, "Checkout fails"));

        result.ShouldHaveValidationErrorFor(x => x.ApplicationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyTitle_HasTitleError(string title)
    {
        var result = _validator.TestValidate(new FindSimilarIncidentsQuery(Guid.NewGuid(), title));

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_TitleTooLong_HasTitleError()
    {
        var result = _validator.TestValidate(new FindSimilarIncidentsQuery(Guid.NewGuid(), new string('a', Incident.TitleMaxLength + 1)));

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_TitleAtMaxLengthWithPadding_NoErrors()
    {
        var result = _validator.TestValidate(new FindSimilarIncidentsQuery(Guid.NewGuid(), $" {new string('a', Incident.TitleMaxLength)} "));

        result.ShouldNotHaveValidationErrorFor(x => x.Title);
    }
}
