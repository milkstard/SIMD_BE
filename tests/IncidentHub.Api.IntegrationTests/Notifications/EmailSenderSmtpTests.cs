using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using IncidentHub.Application.Notifications;
using IncidentHub.Infrastructure.Notifications;
using IncidentHub.Infrastructure.Notifications.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;

namespace IncidentHub.Api.IntegrationTests.Notifications;

[Trait("Category", "Integration")]
public sealed class EmailSenderSmtpTests(MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private static EmailSender CreateSender(string host, int port, NotificationResilienceOptions? resilience = null)
    {
        var emailOptions = Options.Create(new EmailOptions
        {
            Enabled = true,
            Host = host,
            Port = port,
            FromAddress = "noreply@incidenthub.local",
            FromName = "IncidentHub",
            PublicBaseUrl = "http://localhost:5173",
        });

        var builder = new ResiliencePipelineBuilder();
        NotificationResilience.Configure(builder, resilience ?? new NotificationResilienceOptions());
        return new EmailSender(new MailKitSmtpTransport(emailOptions), builder.Build(), emailOptions, NullLogger<EmailSender>.Instance);
    }

    [Fact]
    public async Task Send_RealSmtp_DeliversMultipartMessage()
    {
        var id = Guid.NewGuid();
        var subject = $"[INC-1042] Checkout fails {id:N}";
        var sender = CreateSender(mailpit.SmtpHost, mailpit.SmtpPort);

        await sender.SendAsync(
            new Notification(id, "bob@example.com", "Bob", subject, "plain body", "<p>html body &amp; more</p>"),
            CancellationToken.None);

        using var message = await mailpit.WaitForMessageAsync(id.ToString("N"));
        var root = message.RootElement;
        root.GetProperty("To")[0].GetProperty("Address").GetString().Should().Be("bob@example.com");
        root.GetProperty("Subject").GetString().Should().Be(subject);
        root.GetProperty("Text").GetString().Should().Contain("plain body");
        root.GetProperty("HTML").GetString().Should().Contain("html body");
        root.GetProperty("MessageID").GetString().Should().Contain(id.ToString());

        using var headers = await mailpit.GetHeadersAsync(root.GetProperty("ID").GetString()!);
        headers.RootElement.GetProperty("X-Incidenthub-Message")[0].GetString().Should().Be(id.ToString());
    }

    [Fact]
    public async Task Send_SmtpDown_ThrowsDeliveryException()
    {
        var closedPort = FreePort();
        var quick = new NotificationResilienceOptions
        {
            MaxRetries = 1,
            RetryBaseDelay = TimeSpan.FromMilliseconds(10),
            AttemptTimeout = TimeSpan.FromSeconds(5),
            TotalTimeout = TimeSpan.FromSeconds(20),
        };
        var sender = CreateSender("127.0.0.1", closedPort, quick);

        var act = () => sender.SendAsync(
            new Notification(Guid.NewGuid(), "bob@example.com", null, "Down", "t", "<p>h</p>"),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotificationDeliveryException>();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
