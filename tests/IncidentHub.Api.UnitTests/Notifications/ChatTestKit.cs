using System.Net;
using IncidentHub.Application.Notifications;

namespace IncidentHub.Api.UnitTests.Notifications;

/// <summary>The webhook URLs carry secret-looking path and query parts so tests can assert they never leak.</summary>
internal static class ChatTestKit
{
    public const string SecretPath = "SECRETPATH123";

    public const string TeamsUrl = "https://prod-12.westus.logic.azure.com:443/workflows/" + SecretPath + "/triggers/manual/paths/invoke?sig=SECRETSIG";

    public const string SlackUrl = "https://hooks.slack.com/services/T000/B000/" + SecretPath;

    public static Notification Message(string destination, string subject = "[High] INC-1042 Checkout fails", string text = "Application: Checkout\nOpen: https://app.example.com/incidents/INC-1042") =>
        new(Guid.NewGuid(), destination, null, subject, text, string.Empty);
}

/// <summary>Records every request and answers with <see cref="Respond"/>.</summary>
internal sealed class FakeWebhookHandler : HttpMessageHandler
{
    public List<(HttpMethod Method, Uri Uri, string? ContentType, string Body)> Requests { get; } = [];

    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    /// <summary>When true the request never completes until it is cancelled.</summary>
    public bool Hang { get; set; }

    public void Always(HttpStatusCode status) => Respond = _ => Task.FromResult(new HttpResponseMessage(status));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!, request.Content?.Headers.ContentType?.MediaType, await request.Content!.ReadAsStringAsync(cancellationToken)));
        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return await Respond(request);
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
