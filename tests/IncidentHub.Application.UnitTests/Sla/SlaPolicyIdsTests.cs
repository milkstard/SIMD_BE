using FluentAssertions;
using IncidentHub.Application.Sla;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.UnitTests.Sla;

public sealed class SlaPolicyIdsTests
{
    [Theory]
    [InlineData("Critical", Severity.Critical)]
    [InlineData("high", Severity.High)]
    [InlineData("MEDIUM", Severity.Medium)]
    [InlineData("Low", Severity.Low)]
    public void TryParse_SeverityName_ReturnsSeverity(string id, Severity expected)
    {
        SlaPolicyIds.TryParse(id, out var severity).Should().BeTrue();

        severity.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("Bogus")]
    [InlineData("Critical,High")]
    [InlineData(" Critical")]
    public void TryParse_NotASeverityName_ReturnsFalse(string? id)
    {
        SlaPolicyIds.TryParse(id, out _).Should().BeFalse();
    }
}
