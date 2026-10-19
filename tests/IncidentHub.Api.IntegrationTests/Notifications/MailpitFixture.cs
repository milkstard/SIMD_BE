using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace IncidentHub.Api.IntegrationTests.Notifications;

/// <summary>A Mailpit container (same image as docker-compose): SMTP catcher plus an HTTP API to read what arrived.</summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private const string Image = "axllent/mailpit:v1.21.8";

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage(Image)
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/readyz")))
        .Build();

    private readonly HttpClient _http = new();

    public string SmtpHost => _container.Hostname;

    public int SmtpPort => _container.GetMappedPublicPort(1025);

    private string ApiBase => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8025)}/api/v1";

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>Waits (up to 15 s) for a message whose subject contains <paramref name="subjectFragment"/>; returns its parsed JSON.</summary>
    public async Task<JsonDocument> WaitForMessageAsync(string subjectFragment)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var search = JsonDocument.Parse(
                await _http.GetStringAsync($"{ApiBase}/search?query={Uri.EscapeDataString($"subject:\"{subjectFragment}\"")}"));
            var messages = search.RootElement.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                var id = messages[0].GetProperty("ID").GetString();
                return JsonDocument.Parse(await _http.GetStringAsync($"{ApiBase}/message/{id}"));
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"No message with subject containing '{subjectFragment}' reached Mailpit.");
    }

    /// <summary>Every message whose subject contains <paramref name="subjectFragment"/> right now (no waiting): subject and first recipient.</summary>
    public async Task<IReadOnlyList<(string Subject, string To)>> ListAsync(string subjectFragment)
    {
        using var search = JsonDocument.Parse(
            await _http.GetStringAsync($"{ApiBase}/search?query={Uri.EscapeDataString($"subject:\"{subjectFragment}\"")}&limit=200"));
        return search.RootElement.GetProperty("messages").EnumerateArray()
            .Select(m => (m.GetProperty("Subject").GetString()!, m.GetProperty("To")[0].GetProperty("Address").GetString()!))
            .ToList();
    }

    /// <summary>The <c>Message-Id</c> of every message whose subject contains <paramref name="subjectFragment"/> right now, oldest first.</summary>
    public async Task<IReadOnlyList<string>> ListMessageIdsAsync(string subjectFragment)
    {
        using var search = JsonDocument.Parse(
            await _http.GetStringAsync($"{ApiBase}/search?query={Uri.EscapeDataString($"subject:\"{subjectFragment}\"")}&limit=200"));
        return search.RootElement.GetProperty("messages").EnumerateArray()
            .Reverse()
            .Select(m => m.GetProperty("MessageID").GetString()!)
            .ToList();
    }

    /// <summary>Returns the raw header map of a message (header name to values).</summary>
    public async Task<JsonDocument> GetHeadersAsync(string messageId) =>
        JsonDocument.Parse(await _http.GetStringAsync($"{ApiBase}/message/{messageId}/headers"));
}
