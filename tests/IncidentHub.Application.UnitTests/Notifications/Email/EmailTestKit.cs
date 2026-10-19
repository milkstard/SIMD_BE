using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using NSubstitute;

namespace IncidentHub.Application.UnitTests.Notifications.Email;

/// <summary>Shared fakes for the email handler tests: a reader, a sender that records, and a renderer that returns fixed parts.</summary>
internal sealed class EmailTestKit
{
    public static readonly DateTimeOffset At = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    public EmailTestKit()
    {
        Sender.Channel.Returns(NotificationChannel.Email);
        Renderer.Render(Arg.Any<string>(), Arg.Any<IReadOnlyList<(string Label, string Value)>>(), Arg.Any<string>())
            .Returns(new RenderedEmail("text", "<p>html</p>"));
        Delivery = new EmailDelivery([Sender], Renderer);
    }

    public IIncidentEmailReader Reader { get; } = Substitute.For<IIncidentEmailReader>();

    public INotificationSender Sender { get; } = Substitute.For<INotificationSender>();

    public IEmailTemplateRenderer Renderer { get; } = Substitute.For<IEmailTemplateRenderer>();

    public EmailDelivery Delivery { get; }

    public static IncidentEmailFacts Facts(
        Guid? reporterId = null,
        Guid? teamId = null,
        string title = "Checkout fails",
        IncidentStatus status = IncidentStatus.Resolved,
        Guid? assigneeId = null,
        DateTimeOffset? slaWarnedAt = null,
        Guid? escalationUserId = null,
        int? escalateAtPercent = 80) => new(
        "INC-1042",
        title,
        "Checkout",
        AppEnvironment.Production,
        Severity.High,
        status,
        reporterId ?? Guid.NewGuid(),
        teamId ?? Guid.NewGuid(),
        assigneeId,
        ResolveDueAt: At.AddHours(4),
        SlaWarnedAt: slaWarnedAt,
        EscalationUserId: escalationUserId,
        EscalateAtPercent: escalateAtPercent);

    public IReadOnlyList<Notification> Sent() =>
        Sender.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(INotificationSender.SendAsync))
            .Select(c => (Notification)c.GetArguments()[0]!)
            .ToList();
}
