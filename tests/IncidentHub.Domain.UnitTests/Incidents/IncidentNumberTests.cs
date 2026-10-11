using FluentAssertions;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Domain.UnitTests.Incidents;

public sealed class IncidentNumberTests
{
    [Fact]
    public void ToString_PositiveNumber_FormatsWithIncPrefix() =>
        new IncidentNumber(1042).ToString().Should().Be("INC-1042");

    [Theory]
    [InlineData("INC-1042", 1042)]
    [InlineData("inc-7", 7)]
    public void TryParse_ValidText_ReturnsNumber(string text, int expected)
    {
        IncidentNumber.TryParse(text, out var number).Should().BeTrue();

        number.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1042")]
    [InlineData("INC-")]
    [InlineData("INC-0")]
    [InlineData("INC--5")]
    [InlineData("INC-12x")]
    [InlineData("INC-99999999999")]
    public void TryParse_InvalidInput_ReturnsFalse(string? text) =>
        IncidentNumber.TryParse(text, out _).Should().BeFalse();
}
