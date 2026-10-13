using FluentAssertions;
using FluentValidation;
using IncidentHub.Application.Incidents.ListIncidents;

namespace IncidentHub.Application.UnitTests.Incidents.ListIncidents;

public sealed class IncidentCursorTests
{
    [Theory]
    [InlineData(IncidentSort.CreatedAtDesc)]
    [InlineData(IncidentSort.CreatedAtAsc)]
    [InlineData(IncidentSort.Severity)]
    [InlineData(IncidentSort.UpdatedAtDesc)]
    public void Decode_EncodedCursor_RoundTrips(IncidentSort sort)
    {
        var cursor = new IncidentCursor(sort, new DateTimeOffset(2026, 10, 13, 8, 30, 0, TimeSpan.Zero), 2, Guid.NewGuid());

        var decoded = IncidentCursor.DecodeOrThrow(cursor.Encode(), sort);

        decoded.Should().Be(cursor);
    }

    [Fact]
    public void DecodeOrThrow_NoCursor_ReturnsNull()
    {
        IncidentCursor.DecodeOrThrow(null, IncidentSort.CreatedAtDesc).Should().BeNull();
        IncidentCursor.DecodeOrThrow("  ", IncidentSort.CreatedAtDesc).Should().BeNull();
    }

    [Fact]
    public void DecodeOrThrow_SortMismatch_ThrowsValidationOnCursor()
    {
        var cursor = new IncidentCursor(IncidentSort.Severity, DateTimeOffset.UtcNow, 1, Guid.NewGuid()).Encode();

        var act = () => IncidentCursor.DecodeOrThrow(cursor, IncidentSort.CreatedAtDesc);

        act.Should().Throw<ValidationException>().Which.Errors.Should().ContainSingle(e => e.PropertyName == "cursor");
    }

    [Theory]
    [InlineData("%%%")]
    [InlineData("e30")]
    [InlineData("bm90LWpzb24")]
    public void DecodeOrThrow_Garbage_ThrowsValidationOnCursor(string cursor)
    {
        var act = () => IncidentCursor.DecodeOrThrow(cursor, IncidentSort.CreatedAtDesc);

        act.Should().Throw<ValidationException>();
    }
}
