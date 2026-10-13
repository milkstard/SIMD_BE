using FluentValidation.TestHelper;
using IncidentHub.Application.Incidents.ListIncidents;

namespace IncidentHub.Application.UnitTests.Incidents.ListIncidents;

public sealed class ListIncidentsValidatorTests
{
    private readonly ListIncidentsValidator _validator = new();

    private static ListIncidentsQuery Valid() =>
        new([], [], null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Validate_ValidQuery_NoErrors()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void Validate_LimitOutOfRange_HasLimitError(int limit)
    {
        _validator.TestValidate(Valid() with { Limit = limit }).ShouldHaveValidationErrorFor(x => x.Limit);
    }

    [Fact]
    public void Validate_DateRangeReversed_HasCreatedToError()
    {
        var from = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

        var result = _validator.TestValidate(Valid() with { CreatedFrom = from, CreatedTo = from.AddDays(-1) });

        result.ShouldHaveValidationErrorFor(x => x.CreatedTo);
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Validate_BadAssigneeToken_HasAssigneeIdError(string value)
    {
        _validator.TestValidate(Valid() with { AssigneeId = value }).ShouldHaveValidationErrorFor(x => x.AssigneeId);
    }

    [Theory]
    [InlineData("me")]
    [InlineData("unassigned")]
    [InlineData("ME")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    public void Validate_GoodAssigneeToken_NoError(string value)
    {
        _validator.TestValidate(Valid() with { AssigneeId = value }).ShouldNotHaveValidationErrorFor(x => x.AssigneeId);
    }

    [Theory]
    [InlineData("unassigned")]
    [InlineData("someone")]
    public void Validate_BadReporterToken_HasReporterIdError(string value)
    {
        _validator.TestValidate(Valid() with { ReporterId = value }).ShouldHaveValidationErrorFor(x => x.ReporterId);
    }

    [Fact]
    public void Validate_QTooLong_HasQError()
    {
        var result = _validator.TestValidate(Valid() with { Q = new string('x', ListIncidentsQuery.MaxQueryLength + 1) });

        result.ShouldHaveValidationErrorFor(x => x.Q);
    }

    [Fact]
    public void Validate_GarbageCursor_HasCursorError()
    {
        _validator.TestValidate(Valid() with { Cursor = "not-a-cursor" }).ShouldHaveValidationErrorFor(x => x.Cursor);
    }

    [Fact]
    public void Validate_CursorOfAnotherSort_HasCursorError()
    {
        var cursor = new IncidentCursor(IncidentSort.CreatedAtAsc, DateTimeOffset.UtcNow, 0, Guid.NewGuid()).Encode();

        var result = _validator.TestValidate(Valid() with { Sort = IncidentSort.UpdatedAtDesc, Cursor = cursor });

        result.ShouldHaveValidationErrorFor(x => x.Cursor);
    }

    [Fact]
    public void Validate_CursorOfSameSort_NoError()
    {
        var cursor = new IncidentCursor(IncidentSort.Severity, DateTimeOffset.UtcNow, 2, Guid.NewGuid()).Encode();

        _validator.TestValidate(Valid() with { Sort = IncidentSort.Severity, Cursor = cursor })
            .ShouldNotHaveValidationErrorFor(x => x.Cursor);
    }
}
