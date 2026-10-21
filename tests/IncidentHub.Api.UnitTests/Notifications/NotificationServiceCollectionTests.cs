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

    private static INotificationSender EmailSender(IServiceProvider provider) =>
        provider.GetServices<INotificationSender>().Single(s => s.Channel == NotificationChannel.Email);

    private static INotificationSender ChatSender(IServiceProvider provider, NotificationChannel channel) =>
        provider.GetServices<INotificationSender>().Single(s => s.Channel == channel);

    [Fact]
    public void Resolve_Disabled_UsesLoggingSenderWithoutAnySmtpSettings()
    {
        using var provider = Build(new() { ["Notifications:Email:Enabled"] = "false" });

        provider.GetRequiredService<IStartupValidator>().Validate();

        var sender = EmailSender(provider);
        sender.GetType().Name.Should().Be("LoggingEmailSender");
        sender.Channel.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public async Task SendAsync_Disabled_SendsNothingAndDoesNotThrow()
    {
        using var provider = Build(new() { ["Notifications:Email:Enabled"] = "false" });
        var sender = EmailSender(provider);

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

        EmailSender(provider).GetType().Name.Should().Be("EmailSender");
    }

    [Fact]
    public void Validate_BadResilienceOptions_FailsNamingTheField()
    {
        using var provider = Build(new() { ["Notifications:Resilience:MaxRetries"] = "50" });

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("Notifications:Resilience:MaxRetries");
    }

    [Fact]
    public async Task AddNotificationSenders_ChatDisabled_RegistersLoggingSenders()
    {
        using var provider = Build(new());

        provider.GetRequiredService<IStartupValidator>().Validate();

        ChatSender(provider, NotificationChannel.Teams).GetType().Name.Should().Be("LoggingChatSender");
        ChatSender(provider, NotificationChannel.Slack).GetType().Name.Should().Be("LoggingChatSender");
        var act = () => ChatSender(provider, NotificationChannel.Slack).SendAsync(
            new Notification(Guid.NewGuid(), "https://hooks.slack.com/services/T/B/X", null, "Subject", "t", string.Empty), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void AddNotificationSenders_ChatEnabledAndValid_RegistersWebhookSenders()
    {
        using var provider = Build(new() { ["Notifications:Chat:Enabled"] = "true" });

        provider.GetRequiredService<IStartupValidator>().Validate();

        ChatSender(provider, NotificationChannel.Teams).GetType().Name.Should().Be("TeamsNotificationSender");
        ChatSender(provider, NotificationChannel.Slack).GetType().Name.Should().Be("SlackNotificationSender");
        provider.GetServices<INotificationSender>().Select(s => s.Channel).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AddNotificationSenders_ChatEnabledInvalid_FailsOnStart()
    {
        using var provider = Build(new()
        {
            ["Notifications:Chat:Enabled"] = "true",
            ["Notifications:Chat:AllowedHosts:0"] = "*.com",
        });

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("Notifications:Chat:AllowedHosts");
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void Resolve_ChatSenders_AreLiveOnlyWhenChatIsEnabled(string enabled, bool live)
    {
        using var provider = Build(new() { ["Notifications:Chat:Enabled"] = enabled });

        ChatSender(provider, NotificationChannel.Teams).IsLive.Should().Be(live);
        ChatSender(provider, NotificationChannel.Slack).IsLive.Should().Be(live);
    }

    [Fact]
    public void Resolve_Router_IsRegisteredScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NSubstitute.Substitute.For<IAppDbContext>());
        services.AddNotificationSenders(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<NotificationRouter>().Should().NotBeNull();
    }
}
