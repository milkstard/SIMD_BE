using FluentAssertions;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Domain.UnitTests.Incidents;

public sealed class IncidentFingerprintTests
{
    private static readonly Guid AppId = Guid.Parse("b3f10000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData("Checkout fails with 500", "  CHECKOUT   fails\twith 500 ")]
    [InlineData("Checkout fails with 500", "Checkout fails, with 500!")]
    public void Compute_SameTitleDifferentCasingSpacingPunctuation_Equal(string a, string b) =>
        IncidentFingerprint.Compute(AppId, a).Should().Be(IncidentFingerprint.Compute(AppId, b));

    [Fact]
    public void Compute_DifferentApplication_Differs() =>
        IncidentFingerprint.Compute(AppId, "Checkout fails")
            .Should().NotBe(IncidentFingerprint.Compute(Guid.NewGuid(), "Checkout fails"));

    [Fact]
    public void Compute_DifferentTitle_Differs() =>
        IncidentFingerprint.Compute(AppId, "Checkout fails")
            .Should().NotBe(IncidentFingerprint.Compute(AppId, "Login fails"));

    [Fact]
    public void Compute_AnyInput_Returns64LowercaseHexChars() =>
        IncidentFingerprint.Compute(AppId, "Checkout fails").Should().MatchRegex("^[0-9a-f]{64}$");
}
