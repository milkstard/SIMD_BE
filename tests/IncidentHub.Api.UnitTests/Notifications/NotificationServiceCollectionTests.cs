using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
using IncidentHub.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IncidentHub.Api.UnitTests.Notifications;

public sealed class NotificationServiceCollectionTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationSenders(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Resolve_Disabled_UsesLoggingSenderWithoutAnySmtpSettings()
    {
        using var provider = Build(new() { ["Notifications:Email:Enabled"] = "false" });

        provider.GetRequiredService<IStartupValidator>().Validate();

        var sender = provider.GetRequiredService<INotificationSender>();
        sender.GetType().Name.Should().Be("LoggingEmailSender");
        sender.Channel.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public async Task SendAsync_Disabled_SendsNothingAndDoesNotThrow()
    {
        using var provider = Build(new() { ["Notifications:Email:Enabled"] = "false" });
        var sender = provider.GetRequiredService<INotificationSender>();

        var act = () => sender.SendAsync(new Notification(Guid.NewGuid(), "a@example.com", null, "Subject", "t", "<p>h</p>"), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Validate_EnabledWithoutHost_FailsNamingTheField()
    {
        using var provider = Build(new()
        {
            ["Notifications:Email:Enabled"] = "true",
            ["Notifications:Email:FromAddress"] = "noreply@example.com",
            ["Notifications:Email:PublicBaseUrl"] = "https://app.example.com",
        });

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("Notifications:Email:Host");
    }

    [Fact]
    public void Resolve_EnabledAndValid_UsesEmailSender()
    {
        using var provider = Build(new()
        {
            ["Notifications:Email:Enabled"] = "true",
            ["Notifications:Email:Host"] = "localhost",
            ["Notifications:Email:FromAddress"] = "noreply@example.com",
            ["Notifications:Email:PublicBaseUrl"] = "https://app.example.com",
        });

        provider.GetRequiredService<IStartupValidator>().Validate();

        provider.GetRequiredService<INotificationSender>().GetType().Name.Should().Be("EmailSender");
    }

    [Fact]
    public void Validate_BadResilienceOptions_FailsNamingTheField()
    {
        using var provider = Build(new() { ["Notifications:Resilience:MaxRetries"] = "50" });

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("Notifications:Resilience:MaxRetries");
    }
}
