using System.Reflection;
using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Notifications;
using IncidentHub.Infrastructure.Notifications.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.UnitTests.Architecture;

/// <summary>Request handlers, controllers and hubs never send mail: only outbox handlers (through <see cref="EmailDelivery"/> and <see cref="ChatDelivery"/>) do.</summary>
public sealed class NotificationLayeringTests
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(IIncidentEmailReader).Assembly,
        typeof(NotificationServiceCollectionExtensions).Assembly,
        typeof(IncidentHub.Api.Auth.Policies).Assembly,
    ];

    [Fact]
    public void OnlyOutboxHandlers_ReferenceNotificationSender()
    {
        var offenders = Assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass
                && !typeof(IOutboxMessageHandler).IsAssignableFrom(t)
                && t != typeof(EmailDelivery)
                && t != typeof(ChatDelivery))
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Any(p => IsSender(p.ParameterType)))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty("only dispatcher-run handlers may depend on INotificationSender");
    }

    [Fact]
    public void OnlyTheChatSenders_ReferenceTheHttpClient()
    {
        var offenders = Assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass
                && t.Namespace != typeof(ChatOptions).Namespace)
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(IHttpClientFactory) || p.ParameterType == typeof(HttpClient)))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty("only the chat webhook senders make outbound webhook calls");
    }

    [Fact]
    public void Router_DoesNotDependOnSendersDirectly()
    {
        typeof(NotificationRouter).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Should().NotContain(p => IsSender(p.ParameterType), "the router goes through EmailDelivery and ChatDelivery");
    }

    [Fact]
    public void Api_DoesNotRegisterChatSenders()
    {
        var apiAssembly = typeof(IncidentHub.Api.Auth.Policies).Assembly;
        var offenders = apiAssembly.GetTypes()
            .Where(t => t.IsClass
                && (typeof(INotificationSender).IsAssignableFrom(t)
                    || t == typeof(ChatDelivery)
                    || t == typeof(NotificationRouter)
                    || t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .SelectMany(c => c.GetParameters())
                        .Any(p => p.ParameterType == typeof(ChatDelivery) || p.ParameterType == typeof(NotificationRouter))))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty("only the Worker host registers senders; the API sends nothing");
    }

    [Fact]
    public void AddNotificationSenders_RegistersTheEmailHandlersKeyedByEventType()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NSubstitute.Substitute.For<IAppDbContext>());
        services.AddNotificationSenders(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        foreach (var (key, type) in new[]
        {
            (nameof(IncidentReported), typeof(IncidentReportedEmailHandler)),
            (nameof(IncidentAssigned), typeof(IncidentAssignedEmailHandler)),
            (nameof(IncidentTransitioned), typeof(IncidentResolvedEmailHandler)),
            (nameof(SlaWarning), typeof(SlaWarningEmailHandler)),
            (nameof(SlaBreached), typeof(SlaBreachedEmailHandler)),
        })
        {
            var handler = scope.ServiceProvider.GetRequiredKeyedService<IOutboxMessageHandler>(key);
            handler.Should().BeOfType(type);
            handler.EventType.Should().Be(key);
        }

        scope.ServiceProvider.GetRequiredService<NotificationRouter>().Should().NotBeNull();
    }

    private static bool IsSender(Type type) =>
        type == typeof(INotificationSender)
        || (type.IsGenericType && type.GetGenericArguments().Any(a => a == typeof(INotificationSender)));
}
