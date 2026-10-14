using System.Reflection;
using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Worker.Jobs;

namespace IncidentHub.Worker.UnitTests.Jobs;

public sealed class WorkerNotificationLayeringTests
{
    [Fact]
    public void WorkerJobs_DoNotDependOnNotificationSender()
    {
        var offenders = typeof(OutboxDispatcher).Assembly.GetTypes()
            .Where(t => t.IsClass)
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(INotificationSender)
                    || (p.ParameterType.IsGenericType && p.ParameterType.GetGenericArguments().Contains(typeof(INotificationSender)))))
            .Select(t => t.FullName);

        offenders.Should().BeEmpty("mail is sent only by the outbox handlers the dispatcher runs");
    }
}
