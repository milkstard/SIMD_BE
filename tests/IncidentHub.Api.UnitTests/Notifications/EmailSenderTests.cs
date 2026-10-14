using FluentAssertions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Outbox;
using IncidentHub.Infrastructure.Notifications;
using MailKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Polly;

namespace IncidentHub.Api.UnitTests.Notifications;

public sealed class EmailSenderTests
{
    private readonly ISmtpTransport _transport = Substitute.For<ISmtpTransport>();
    private MimeMessage? _sent;

    private EmailSender Create(ResiliencePipeline? pipeline = null)
    {
        _transport.SendAsync(Arg.Do<MimeMessage>(m => _sent = m), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var options = Options.Create(new EmailOptions { FromAddress = "noreply@incidenthub.local", FromName = "IncidentHub" });
        return new EmailSender(_transport, pipeline ?? ResiliencePipeline.Empty, options, NullLogger<EmailSender>.Instance);
    }

    private static Notification Mail(Guid? id = null, string subject = "New incident", string to = "bob@example.com") =>
        new(id ?? Guid.NewGuid(), to, "Bob", subject, "plain text", "<p>html</p>");

    [Fact]
    public async Task Send_ValidNotification_BuildsMultipartMessage()
    {
        await Create().SendAsync(Mail(), CancellationToken.None);

        _sent.Should().NotBeNull();
        _sent!.To.Mailboxes.Single().Address.Should().Be("bob@example.com");
        _sent.Subject.Should().Be("New incident");
        _sent.TextBody.Should().Be("plain text");
        _sent.HtmlBody.Should().Be("<p>html</p>");
        _sent.From.Mailboxes.Single().Address.Should().Be("noreply@incidenthub.local");
    }

    [Fact]
    public async Task Send_CrLfInSubject_IsStripped()
    {
        await Create().SendAsync(Mail(subject: "Hello\r\nBcc: evil@example.com"), CancellationToken.None);

        _sent!.Subject.Should().NotContain("\r").And.NotContain("\n");
        _sent.Headers.Contains(HeaderId.Bcc).Should().BeFalse();
        _sent.Bcc.Count.Should().Be(0);
    }

    [Fact]
    public async Task Send_SetsMessageIdFromOutboxId()
    {
        var id = Guid.NewGuid();

        await Create().SendAsync(Mail(id), CancellationToken.None);

        _sent!.MessageId.Should().Be($"{id}@incidenthub");
        _sent.Headers["X-IncidentHub-Message"].Should().Be(id.ToString());
    }

    [Fact]
    public async Task Send_PermanentReply_ThrowsNonRetryableWithoutAddress()
    {
        var sender = Create();
        _transport.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>()).Throws(
            new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 no such user bob@example.com"));

        var act = () => sender.SendAsync(Mail(), CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<NonRetryableOutboxException>()).Which;
        exception.Message.Should().Contain("550").And.Contain("example.com");
        exception.Message.Should().NotContain("bob").And.NotContain("no such user");
        exception.InnerException.Should().BeNull();
    }

    [Theory]
    [InlineData(SmtpStatusCode.MailboxBusy)]
    [InlineData(SmtpStatusCode.ServiceNotAvailable)]
    [InlineData(SmtpStatusCode.AuthenticationRequired)]
    public async Task Send_TransientOrAuthReply_ThrowsDeliveryException(SmtpStatusCode status)
    {
        var sender = Create();
        _transport.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>()).Throws(
            new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, status, "reply mentioning bob@example.com"));

        var act = () => sender.SendAsync(Mail(), CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which;
        exception.Message.Should().NotContain("bob").And.NotContain("reply mentioning");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task Send_ConnectionFailure_ThrowsDeliveryException()
    {
        var sender = Create();
        _transport.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>()).Throws(new IOException("connection refused"));

        var act = () => sender.SendAsync(Mail(), CancellationToken.None);

        await act.Should().ThrowAsync<NotificationDeliveryException>();
    }

    [Fact]
    public async Task Send_UnexpectedFailure_ErrorTextCarriesNoServerReplyOrAddress()
    {
        var sender = Create();
        _transport.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>()).Throws(
            new MailKit.Security.AuthenticationException("535 5.7.8 bob@example.com secret reply"));

        var act = () => sender.SendAsync(Mail(), CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which;
        exception.Message.Should().NotContain("bob").And.NotContain("secret reply");
        exception.InnerException.Should().BeNull("the dispatcher logs the whole exception chain");
    }

    [Fact]
    public async Task Send_InvalidAddress_ThrowsNonRetryableWithoutAddress()
    {
        var sender = Create();

        var act = () => sender.SendAsync(Mail(to: "not an address@bad"), CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<NonRetryableOutboxException>()).Which;
        exception.Message.Should().NotContain("not an address");
        await _transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task Send_Cancelled_PropagatesCancellation()
    {
        var sender = Create();
        _transport.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>()).Throws(new OperationCanceledException());

        var act = () => sender.SendAsync(Mail(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
