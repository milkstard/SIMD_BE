using FluentAssertions;
using IncidentHub.Infrastructure.Notifications.Chat;

namespace IncidentHub.Api.UnitTests.Notifications;

public sealed class ChatOptionsValidatorTests
{
    private readonly ChatOptionsValidator _validator = new();

    [Fact]
    public void Validate_Disabled_Succeeds()
    {
        _validator.Validate(null, new ChatOptions { Enabled = false, AllowedHosts = ["*.com", "bad host"] }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_EnabledWithoutConfiguredHosts_UsesDefaultsAndSucceeds()
    {
        var options = new ChatOptions { Enabled = true };

        options.EffectiveAllowedHosts.Should().Contain("hooks.slack.com");
        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_EnabledWithoutAllowedHosts_Fails()
    {
        // Only reachable if the defaults are emptied; AllowedHosts entries that are blank count as invalid.
        var result = _validator.Validate(null, new ChatOptions { Enabled = true, AllowedHosts = [" "] });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Notifications:Chat:AllowedHosts");
    }

    [Theory]
    [InlineData("*.com")]
    [InlineData("https://hooks.slack.com")]
    [InlineData("a*.example.com")]
    [InlineData("bad host")]
    public void Validate_BadHostPattern_Fails(string pattern)
    {
        var result = _validator.Validate(null, new ChatOptions { Enabled = true, AllowedHosts = [pattern] });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Notifications:Chat:AllowedHosts").And.Contain(pattern);
    }

    [Theory]
    [InlineData("hooks.slack.com")]
    [InlineData("*.webhook.office.com")]
    public void Validate_GoodHostPattern_Succeeds(string pattern)
    {
        _validator.Validate(null, new ChatOptions { Enabled = true, AllowedHosts = [pattern] }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("contoso.webhook.office.com", true)]
    [InlineData("CONTOSO.Webhook.Office.com", true)]
    [InlineData("a.b.webhook.office.com", true)]
    [InlineData("webhook.office.com", false)]
    [InlineData("evilwebhook.office.com", false)]
    [InlineData("webhook.office.com.evil.example", false)]
    public void Allows_WildcardMatchesWholeLabelsOnly(string host, bool expected)
    {
        HostAllowList.Allows(host, ["*.webhook.office.com"]).Should().Be(expected);
    }
}
