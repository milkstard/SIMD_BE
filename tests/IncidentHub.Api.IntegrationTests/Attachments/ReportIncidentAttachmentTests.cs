using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IncidentHub.Api.IntegrationTests.Attachments.AttachmentTestSupport;
using static IncidentHub.Api.IntegrationTests.Incidents.IncidentWriteHelpers;

namespace IncidentHub.Api.IntegrationTests.Attachments;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReportIncidentAttachmentTests(ApiFactory factory)
{
    private const string Route = "/api/v1/incidents";

    private sealed record Reporter(HttpClient Client, Guid UserId, Guid AppId);

    private async Task<Reporter> ReporterAsync()
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var userId = await factory.EnsureUserAsync(oid);
        return new Reporter(factory.CreateClient().AsUser(factory, oid, UserRole.Responder), userId, appId);
    }

    private static object Body(Guid appId, IEnumerable<Guid> attachmentIds, string? title = null) => new
    {
        applicationId = appId,
        environment = "Production",
        severity = "High",
        title = title ?? $"Checkout fails {Guid.NewGuid():N}",
        description = "<p>Error page after paying.</p>",
        attachmentIds,
    };

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return client.SendAsync(request);
    }

    private static async Task<string> AttachmentIdsErrorAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(response);
        return problem.GetProperty("errors").GetProperty("attachmentIds")[0].GetString()!;
    }

    private async Task<int> IncidentCountAsync(Guid appId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Incidents.CountAsync(i => i.ApplicationId == appId);
    }

    [Fact]
    public async Task Report_WithAttachments_LinksAndReturnsThem()
    {
        var r = await ReporterAsync();
        var first = await SeedAsync(factory, r.UserId, ScanStatus.Clean, fileName: "first.png");
        var second = await SeedAsync(factory, r.UserId, ScanStatus.Pending, fileName: "second.png");

        var response = await PostAsync(r.Client, Body(r.AppId, [first.Id, second.Id]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await JsonAsync(response);
        var files = detail.GetProperty("attachments").EnumerateArray().ToList();
        files.Select(f => f.GetProperty("fileName").GetString()).Should().Equal("first.png", "second.png");
        files.Select(f => f.GetProperty("scanStatus").GetString()).Should().Equal("Clean", "Pending");
        files[0].GetProperty("uploadedBy").GetProperty("id").GetGuid().Should().Be(r.UserId);
        var incidentId = detail.GetProperty("id").GetGuid();
        (await LoadAsync(factory, first.Id)).IncidentId.Should().Be(incidentId);
        (await LoadAsync(factory, second.Id)).CommentId.Should().BeNull();
    }

    [Fact]
    public async Task Report_WithAttachments_DetailEndpointReturnsThemToo()
    {
        var r = await ReporterAsync();
        var file = await SeedAsync(factory, r.UserId, ScanStatus.Clean);
        var detail = await JsonAsync(await PostAsync(r.Client, Body(r.AppId, [file.Id])));

        var read = await r.Client.GetAsync($"{Route}/{detail.GetProperty("number").GetString()}");

        (await JsonAsync(read)).GetProperty("attachments").EnumerateArray()
            .Select(f => f.GetProperty("id").GetGuid()).Should().Equal(file.Id);
    }

    [Fact]
    public async Task Report_WritesHistoryAndAttachmentOutboxInSameSave()
    {
        var r = await ReporterAsync();
        var file = await SeedAsync(factory, r.UserId, ScanStatus.Clean, fileName: "trace.log", contentType: "text/plain", content: "x"u8.ToArray());

        var detail = await JsonAsync(await PostAsync(r.Client, Body(r.AppId, [file.Id])));

        var incidentId = detail.GetProperty("id").GetGuid();
        var history = await HistoryAsync(factory, incidentId);
        history.Select(h => h.Field).Should().BeEquivalentTo("Status", Incident.AttachmentHistoryField);
        history.Single(h => h.Field == Incident.AttachmentHistoryField).NewValue.Should().Be("trace.log");
        var outbox = await OutboxTypesAsync(factory, incidentId);
        outbox.Should().Contain("IncidentReported").And.Contain("AttachmentAdded");
    }

    [Fact]
    public async Task Report_ForeignAttachment_Returns422AndWritesNothing()
    {
        var r = await ReporterAsync();
        var foreign = await SeedAsync(factory, await factory.EnsureUserAsync(NewOid()), ScanStatus.Clean);

        var response = await PostAsync(r.Client, Body(r.AppId, [foreign.Id]));

        (await AttachmentIdsErrorAsync(response)).Should().NotBeNullOrWhiteSpace();
        (await LoadAsync(factory, foreign.Id)).IncidentId.Should().BeNull();
        (await IncidentCountAsync(r.AppId)).Should().Be(0);
    }

    [Fact]
    public async Task Report_UnknownId_GivesTheSameMessageAsAForeignFile()
    {
        var r = await ReporterAsync();
        var foreign = await SeedAsync(factory, await factory.EnsureUserAsync(NewOid()), ScanStatus.Clean);

        var unknown = await AttachmentIdsErrorAsync(await PostAsync(r.Client, Body(r.AppId, [Guid.NewGuid()])));
        var foreignMessage = await AttachmentIdsErrorAsync(await PostAsync(r.Client, Body(r.AppId, [foreign.Id])));

        unknown.Should().Be(foreignMessage);
    }

    [Fact]
    public async Task Report_AlreadyLinkedAttachment_Returns422()
    {
        var r = await ReporterAsync();
        var file = await SeedAsync(factory, r.UserId, ScanStatus.Clean);
        (await PostAsync(r.Client, Body(r.AppId, [file.Id]))).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await PostAsync(r.Client, Body(r.AppId, [file.Id]));

        await AttachmentIdsErrorAsync(response);
        (await IncidentCountAsync(r.AppId)).Should().Be(1);
    }

    [Fact]
    public async Task Report_InfectedAttachment_Returns422()
    {
        var r = await ReporterAsync();
        var infected = await SeedAsync(factory, r.UserId, ScanStatus.Infected);

        var response = await PostAsync(r.Client, Body(r.AppId, [infected.Id]));

        await AttachmentIdsErrorAsync(response);
        (await LoadAsync(factory, infected.Id)).IncidentId.Should().BeNull();
    }

    [Fact]
    public async Task Report_PendingAttachment_Succeeds()
    {
        var r = await ReporterAsync();
        var pending = await SeedAsync(factory, r.UserId, ScanStatus.Pending);

        var response = await PostAsync(r.Client, Body(r.AppId, [pending.Id]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Report_ElevenAttachments_Returns422()
    {
        var r = await ReporterAsync();

        var response = await PostAsync(r.Client, Body(r.AppId, Enumerable.Range(0, 11).Select(_ => Guid.NewGuid())));

        await AttachmentIdsErrorAsync(response);
    }

    [Fact]
    public async Task Report_TenAttachments_Succeeds()
    {
        var r = await ReporterAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < Attachment.MaxPerAction; i++)
        {
            ids.Add((await SeedAsync(factory, r.UserId, ScanStatus.Clean)).Id);
        }

        var response = await PostAsync(r.Client, Body(r.AppId, ids));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await JsonAsync(response)).GetProperty("attachments").GetArrayLength().Should().Be(10);
    }

    [Fact]
    public async Task Report_DuplicateIds_Returns422()
    {
        var r = await ReporterAsync();
        var file = await SeedAsync(factory, r.UserId, ScanStatus.Clean);

        var response = await PostAsync(r.Client, Body(r.AppId, [file.Id, file.Id]));

        await AttachmentIdsErrorAsync(response);
    }

    [Fact]
    public async Task Report_SameIdempotencyKey_LinksOnce()
    {
        var r = await ReporterAsync();
        var file = await SeedAsync(factory, r.UserId, ScanStatus.Clean);
        var body = Body(r.AppId, [file.Id], title: $"Checkout fails {Guid.NewGuid():N}");
        var key = Guid.NewGuid().ToString();

        var first = await PostAsync(r.Client, body, key);
        var second = await PostAsync(r.Client, body, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var incidentId = (await JsonAsync(first)).GetProperty("id").GetGuid();
        (await JsonAsync(second)).GetProperty("id").GetGuid().Should().Be(incidentId);
        (await IncidentCountAsync(r.AppId)).Should().Be(1);
        (await HistoryAsync(factory, incidentId)).Count(h => h.Field == Incident.AttachmentHistoryField).Should().Be(1);
    }

    [Fact]
    public async Task Report_SaveFails_LeavesTheAttachmentsUnlinked()
    {
        var uploaderId = await factory.EnsureUserAsync(NewOid());
        var file = await SeedAsync(factory, uploaderId, ScanStatus.Clean);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.Attachments.SingleAsync(a => a.Id == file.Id);
            var actor = new Actor(uploaderId, new HashSet<UserRole> { UserRole.Responder });

            // The application does not exist, so the foreign key fails the whole save: the incident, its history,
            // the outbox rows and the attachment link all roll back together.
            db.Incidents.Add(Incident.Report(
                Guid.NewGuid(),
                AppEnvironment.Production,
                Severity.High,
                "Checkout fails",
                "<p>x</p>",
                null,
                IncidentSource.Manual,
                actor,
                DateTimeOffset.UtcNow,
                attachments: [tracked]));
            var save = () => db.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();
        }

        var stored = await LoadAsync(factory, file.Id);
        stored.IncidentId.Should().BeNull();
        stored.CommentId.Should().BeNull();
    }

    [Fact]
    public async Task Report_AttachmentIdsOfAnotherUsersTeammate_StillGivesTheGenericError()
    {
        var r = await ReporterAsync();
        var teammateOid = NewOid();
        await factory.AddToTeamAsync(teammateOid, r.AppId);
        var teammateFile = await SeedAsync(factory, await factory.EnsureUserAsync(teammateOid), ScanStatus.Clean);

        var response = await PostAsync(r.Client, Body(r.AppId, [teammateFile.Id]));

        (await AttachmentIdsErrorAsync(response)).Should().Contain("not available");
    }
}
