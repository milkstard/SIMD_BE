using FluentAssertions;
using FluentValidation;
using IncidentHub.Application.Common.Paging;

namespace IncidentHub.Application.UnitTests.Common;

public sealed class KeysetCursorTests
{
    [Fact]
    public void EncodeDecode_RoundTrip_ReturnsSameValues()
    {
        var id = Guid.NewGuid();

        var cursor = KeysetCursor.Encode("Payments / Core+1", id);
        var decoded = KeysetCursor.TryDecode(cursor, out var name, out var decodedId);

        decoded.Should().BeTrue();
        name.Should().Be("Payments / Core+1");
        decodedId.Should().Be(id);
        cursor.Should().NotContainAny("+", "/", "=");
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("!!!")]
    [InlineData("e30")]
    public void DecodeOrThrow_Garbage_ThrowsValidationOnCursor(string cursor)
    {
        var act = () => KeysetCursor.DecodeOrThrow(cursor);

        act.Should().Throw<ValidationException>().Which.Errors.Should().ContainSingle(e => e.PropertyName == "cursor");
    }

    [Fact]
    public void DecodeOrThrow_Null_ReturnsNull() =>
        KeysetCursor.DecodeOrThrow(null).Should().BeNull();
}
