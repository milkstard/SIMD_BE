using System.Reflection;
using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.UnitTests.Architecture;

/// <summary>Request handlers, controllers and hubs never send mail: only outbox handlers (through <see cref="EmailDelivery"/>) do.</summary>
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
                && t != typeof(EmailDelivery))
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Any(p => IsSender(p.ParameterType)))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty("only dispatcher-run handlers may depend on INotificationSender");
    }

    [Fact]
    public void AddNotificationSenders_RegistersTheThreeEmailHandlersKeyedByEventType()
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
        })
        {
            var handler = scope.ServiceProvider.GetRequiredKeyedService<IOutboxMessageHandler>(key);
            handler.Should().BeOfType(type);
            handler.EventType.Should().Be(key);
        }
    }

    private static bool IsSender(Type type) =>
        type == typeof(INotificationSender)
        || (type.IsGenericType && type.GetGenericArguments().Any(a => a == typeof(INotificationSender)));
}
