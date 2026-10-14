using FluentAssertions;
using IncidentHub.Infrastructure.Notifications;

namespace IncidentHub.Api.UnitTests.Notifications;

public sealed class EmailOptionsValidatorTests
{
    private readonly EmailOptionsValidator _validator = new();

    private static EmailOptions Valid() => new()
    {
        Enabled = true,
        Host = "smtp.example.com",
        Port = 587,
        FromAddress = "noreply@example.com",
        PublicBaseUrl = "https://app.example.com",
    };

    [Fact]
    public void Validate_Valid_Succeeds()
    {
        _validator.Validate(null, Valid()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_Disabled_Succeeds()
    {
        _validator.Validate(null, new EmailOptions { Enabled = false }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_EnabledWithoutHost_Fails()
    {
        var options = Valid();
        options.Host = string.Empty;

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("Host");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an address")]
    public void Validate_InvalidFromAddress_Fails(string from)
    {
        var options = Valid();
        options.FromAddress = from;

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("FromAddress");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Validate_PortOutOfRange_Fails(int port)
    {
        var options = Valid();
        options.Port = port;

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("Port");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://app.example.com")]
    [InlineData("/relative")]
    public void Validate_BadPublicBaseUrl_Fails(string url)
    {
        var options = Valid();
        options.PublicBaseUrl = url;

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("PublicBaseUrl");
    }

    [Fact]
    public void Validate_UsernameWithoutPassword_Fails()
    {
        var options = Valid();
        options.Username = "user";
        options.Tls = EmailTlsMode.StartTls;

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("Password");
    }

    [Fact]
    public void Validate_UsernameWithoutTls_Fails()
    {
        var options = Valid();
        options.Username = "user";
        options.Password = "secret";
        options.Tls = EmailTlsMode.None;

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("Tls").And.Contain("clear text");
    }

    [Theory]
    [InlineData(EmailTlsMode.StartTls)]
    [InlineData(EmailTlsMode.SslOnConnect)]
    public void Validate_UsernameWithTls_Succeeds(EmailTlsMode tls)
    {
        var options = Valid();
        options.Username = "user";
        options.Password = "secret";
        options.Tls = tls;

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_NoCredentialsAndNoTls_Succeeds()
    {
        var options = Valid();
        options.Tls = EmailTlsMode.None;

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_UndefinedTlsValue_Fails()
    {
        var options = Valid();
        options.Tls = (EmailTlsMode)99;

        _validator.Validate(null, options).Failures.Should().ContainSingle().Which.Should().Contain("Tls");
    }
}
