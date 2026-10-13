using FluentValidation.TestHelper;
using IncidentHub.Application.Incidents.ListComments;
using IncidentHub.Application.Incidents.ListHistory;

namespace IncidentHub.Application.UnitTests.Incidents.ListComments;

public sealed class ListCommentsValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_LimitOutOfRange_HasError(int limit)
    {
        new ListCommentsValidator().TestValidate(new ListCommentsQuery(Guid.NewGuid(), null, limit))
            .ShouldHaveValidationErrorFor(x => x.Limit);
        new ListHistoryValidator().TestValidate(new ListHistoryQuery(Guid.NewGuid(), null, limit))
            .ShouldHaveValidationErrorFor(x => x.Limit);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(100)]
    public void Validate_LimitInRange_NoErrors(int limit)
    {
        new ListCommentsValidator().TestValidate(new ListCommentsQuery(Guid.NewGuid(), null, limit))
            .ShouldNotHaveAnyValidationErrors();
        new ListHistoryValidator().TestValidate(new ListHistoryQuery(Guid.NewGuid(), null, limit))
            .ShouldNotHaveAnyValidationErrors();
    }
}
