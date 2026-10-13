using System.Net.Http.Json;
using System.Text.Json;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Incidents;

/// <summary>Shared plumbing for the transition and assignment endpoint tests.</summary>
internal static class IncidentWriteHelpers
{
    public static string Route(string number) => $"/api/v1/incidents/{number}";

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>The current quoted ETag of the incident, read through the detail endpoint.</summary>
    public static async Task<string> ETagAsync(HttpClient client, string number)
    {
        var response = await client.GetAsync(Route(number));
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid id, string action, object body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{id}/{action}") { Content = JsonContent.Create(body) };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/incidents/{id}") { Content = JsonContent.Create(body) };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(request);
    }

    public static async Task<List<IncidentHistory>> HistoryAsync(ApiFactory factory, Guid incidentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.IncidentHistory.AsNoTracking().Where(h => h.IncidentId == incidentId).OrderBy(h => h.CreatedAt).ToListAsync();
    }

    /// <summary>Type names of the outbox rows whose payload mentions the incident id.</summary>
    public static async Task<List<string>> OutboxTypesAsync(ApiFactory factory, Guid incidentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = incidentId.ToString();
        return await db.OutboxMessages.AsNoTracking().Where(m => m.Payload.Contains(id)).Select(m => m.Type).ToListAsync();
    }

    /// <summary>Walks a new incident to <paramref name="target"/> through the domain as <paramref name="actor"/> (a responder).</summary>
    public static void MoveTo(Incident incident, Actor actor, DateTimeOffset at, IncidentStatus target)
    {
        IncidentStatus[] path = target switch
        {
            IncidentStatus.New => [],
            IncidentStatus.Triaged => [IncidentStatus.Triaged],
            IncidentStatus.InProgress => [IncidentStatus.Triaged, IncidentStatus.InProgress],
            IncidentStatus.OnHold => [IncidentStatus.Triaged, IncidentStatus.OnHold],
            IncidentStatus.Resolved => [IncidentStatus.Triaged, IncidentStatus.InProgress, IncidentStatus.Resolved],
            IncidentStatus.Reopened => [IncidentStatus.Triaged, IncidentStatus.InProgress, IncidentStatus.Resolved, IncidentStatus.Reopened],
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

        foreach (var step in path)
        {
            incident.TransitionTo(step, actor, "step", at);
        }
    }
}
