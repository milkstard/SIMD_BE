using FluentAssertions;
using FluentValidation;
using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Incidents.ListHistory;

namespace IncidentHub.Application.UnitTests.Incidents.ListHistory;

public sealed class HistoryFieldNamesTests
{
    [Theory]
    [InlineData("AssigneeId", "Assignee")]
    [InlineData("AssignedTeamId", "Team")]
    [InlineData("ApplicationId", "Application")]
    [InlineData("DuplicateOfId", "Duplicate of")]
    [InlineData("Comment", "Comment")]
    [InlineData("InternalComment", "Internal comment")]
    public void ToDisplayName_KnownField_ReturnsDisplayName(string field, string expected)
    {
        HistoryFieldNames.ToDisplayName(field).Should().Be(expected);
    }

    [Theory]
    [InlineData("Status")]
    [InlineData("Severity")]
    [InlineData("SomethingNew")]
    public void ToDisplayName_OtherField_PassesThrough(string field)
    {
        HistoryFieldNames.ToDisplayName(field).Should().Be(field);
    }
}

public sealed class TimeKeysetCursorTests
{
    [Fact]
    public void DecodeOrThrow_RoundTrip_ReturnsPosition()
    {
        var at = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();

        var decoded = TimeKeysetCursor.DecodeOrThrow(TimeKeysetCursor.Encode(at, id));

        decoded.Should().Be((at, id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DecodeOrThrow_NoCursor_ReturnsNull(string? cursor)
    {
        TimeKeysetCursor.DecodeOrThrow(cursor).Should().BeNull();
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("e30")]
    [InlineData("%%%")]
    public void DecodeOrThrow_InvalidCursor_ThrowsValidationOnCursor(string cursor)
    {
        var act = () => TimeKeysetCursor.DecodeOrThrow(cursor);

        act.Should().Throw<ValidationException>().Which.Errors.Should().Contain(e => e.PropertyName == "cursor");
    }
}
