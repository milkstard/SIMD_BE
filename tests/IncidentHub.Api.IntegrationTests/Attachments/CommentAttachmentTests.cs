using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Incidents.ListComments;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using static IncidentHub.Api.IntegrationTests.Attachments.AttachmentTestSupport;
using static IncidentHub.Api.IntegrationTests.Incidents.IncidentWriteHelpers;

namespace IncidentHub.Api.IntegrationTests.Attachments;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CommentAttachmentTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private sealed record Scenario(Guid AppId, Guid IncidentId, string Number, HttpClient Responder, Guid ResponderId, HttpClient Reporter, Guid ReporterId);

    private async Task<Scenario> SeedScenarioAsync(bool closed = false)
    {
        var appId = await factory.CreateAppAsync();
        var responderOid = NewOid();
        var reporterOid = NewOid();
        await factory.AddToTeamAsync(responderOid, appId);
        await factory.AddToTeamAsync(reporterOid, appId);
        var responderId = await factory.EnsureUserAsync(responderOid);
        var reporterId = await factory.EnsureUserAsync(reporterOid);
        var (incidentId, number) = await _seeder.AddIncidentAsync(
            appId,
            $"Checkout fails {Guid.NewGuid():N}",
            reporterId: reporterId,
            configure: closed ? (incident, actor, at) => incident.TransitionTo(IncidentStatus.Closed, actor, null, at, CloseReason.Invalid) : null);
        return new Scenario(
            appId,
            incidentId,
            number,
            NewClient(factory, responderOid, UserRole.Responder),
            responderId,
            NewClient(factory, reporterOid, UserRole.Reporter),
            reporterId);
    }

    private static Task<HttpResponseMessage> PostCommentAsync(HttpClient client, Guid incidentId, IEnumerable<Guid> attachmentIds, bool isInternal = false) =>
        client.PostAsJsonAsync(
            $"/api/v1/incidents/{incidentId}/comments",
            new { body = "<p>See the attached file</p>", isInternal, attachmentIds });

    private static async Task<List<JsonElement>> CommentsAsync(HttpClient client, Guid incidentId)
    {
        var response = await client.GetAsync($"/api/v1/incidents/{incidentId}/comments");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await JsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
    }

    [Fact]
    public async Task Comment_WithAttachment_ListsOnComment()
    {
        var s = await SeedScenarioAsync();
        var file = await SeedAsync(factory, s.ResponderId, ScanStatus.Clean, fileName: "trace.log", contentType: "text/plain", content: "x"u8.ToArray());

        var response = await PostCommentAsync(s.Responder, s.IncidentId, [file.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await JsonAsync(response);
        var commentId = created.GetProperty("id").GetGuid();
        created.GetProperty("attachments")[0].GetProperty("fileName").GetString().Should().Be("trace.log");
        var stored = await LoadAsync(factory, file.Id);
        stored.IncidentId.Should().Be(s.IncidentId);
        stored.CommentId.Should().Be(commentId);

        var listed = (await CommentsAsync(s.Responder, s.IncidentId)).Single();
        listed.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).Should().Equal(file.Id);
    }

    [Fact]
    public async Task Comment_WithAttachment_WritesHistoryAndAttachmentOutbox()
    {
        var s = await SeedScenarioAsync();
        var file = await SeedAsync(factory, s.ResponderId, ScanStatus.Pending);

        (await PostCommentAsync(s.Responder, s.IncidentId, [file.Id])).StatusCode.Should().Be(HttpStatusCode.Created);

        var history = await HistoryAsync(factory, s.IncidentId);
        history.Select(h => h.Field).Should().Contain(Incident.CommentHistoryField).And.Contain(Incident.AttachmentHistoryField);
        (await OutboxTypesAsync(factory, s.IncidentId)).Should().Contain("CommentAdded").And.Contain("AttachmentAdded");
    }

    [Fact]
    public async Task Comment_OnClosedIncident_LinksAttachment()
    {
        var s = await SeedScenarioAsync(closed: true);
        var file = await SeedAsync(factory, s.ResponderId, ScanStatus.Clean);

        var response = await PostCommentAsync(s.Responder, s.IncidentId, [file.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await LoadAsync(factory, file.Id)).IncidentId.Should().Be(s.IncidentId);
    }

    [Fact]
    public async Task Comment_ForeignAttachment_Returns422AndWritesNothing()
    {
        var s = await SeedScenarioAsync();
        var foreign = await SeedAsync(factory, s.ReporterId, ScanStatus.Clean);
        var commentsBefore = (await CommentsAsync(s.Responder, s.IncidentId)).Count;

        var response = await PostCommentAsync(s.Responder, s.IncidentId, [foreign.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty("attachmentIds", out _).Should().BeTrue();
        (await LoadAsync(factory, foreign.Id)).IncidentId.Should().BeNull();
        (await CommentsAsync(s.Responder, s.IncidentId)).Should().HaveCount(commentsBefore);
    }

    [Fact]
    public async Task Comment_ElevenAttachments_Returns422()
    {
        var s = await SeedScenarioAsync();

        var response = await PostCommentAsync(s.Responder, s.IncidentId, Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Comment_InternalAsReporterWithBadAttachment_Returns403Not422()
    {
        // The permission check comes before the attachments are looked at, so the status does not depend on the ids.
        var s = await SeedScenarioAsync();
        var foreign = await SeedAsync(factory, s.ResponderId, ScanStatus.Clean);

        var response = await PostCommentAsync(s.Reporter, s.IncidentId, [foreign.Id], isInternal: true);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LoadAsync(factory, foreign.Id)).IncidentId.Should().BeNull();
    }

    [Fact]
    public async Task LinkAttachment_TwoContextsLoadedUnlinked_SecondSaveThrowsConcurrency()
    {
        // Both contexts read the file as unlinked, which is the interleaving the parallel-request test only hits by luck:
        // without the concurrency token on IncidentId the second save would silently overwrite the first link.
        var s = await SeedScenarioAsync();
        var file = await SeedAsync(factory, s.ResponderId, ScanStatus.Clean);
        using var scopeA = factory.Services.CreateScope();
        using var scopeB = factory.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        var a = await dbA.Attachments.SingleAsync(x => x.Id == file.Id);
        var b = await dbB.Attachments.SingleAsync(x => x.Id == file.Id);
        a.LinkTo(s.IncidentId, null);
        b.LinkTo(s.IncidentId, null);

        await dbA.SaveChangesAsync();
        var second = () => dbB.SaveChangesAsync();

        await second.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await LoadAsync(factory, file.Id)).CommentId.Should().BeNull();
    }

    [Fact]
    public async Task Comment_InternalAttachment_HiddenFromReporter()
    {
        var s = await SeedScenarioAsync();
        var file = await SeedAsync(factory, s.ResponderId, ScanStatus.Clean);
        (await PostCommentAsync(s.Responder, s.IncidentId, [file.Id], isInternal: true)).StatusCode.Should().Be(HttpStatusCode.Created);

        // The reporter never learns the comment, its attachment, or its history rows exist.
        (await CommentsAsync(s.Reporter, s.IncidentId)).Should().BeEmpty();
        var historyResponse = await s.Reporter.GetAsync($"/api/v1/incidents/{s.IncidentId}/history");
        var historyText = await historyResponse.Content.ReadAsStringAsync();
        historyText.Should().NotContain(file.FileName).And.NotContain("Internal attachment").And.NotContain("Internal comment");
        (await s.Reporter.GetAsync($"/api/v1/attachments/{file.Id}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await s.Reporter.GetAsync($"/api/v1/incidents/{s.Number}")).Content.ReadAsStringAsync()).Should().NotContain(file.Id.ToString());

        // The responder sees all of it, and may download the clean file.
        (await CommentsAsync(s.Responder, s.IncidentId)).Single().GetProperty("attachments").GetArrayLength().Should().Be(1);
        var responderHistory = await (await s.Responder.GetAsync($"/api/v1/incidents/{s.IncidentId}/history")).Content.ReadAsStringAsync();
        responderHistory.Should().Contain("Internal attachment");
        (await s.Responder.GetAsync($"/api/v1/attachments/{file.Id}/download")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Comment_PublicAttachment_IsVisibleToReporterAndDownloadable()
    {
        var s = await SeedScenarioAsync();
        var file = await SeedAsync(factory, s.ResponderId, ScanStatus.Clean);
        (await PostCommentAsync(s.Responder, s.IncidentId, [file.Id])).StatusCode.Should().Be(HttpStatusCode.Created);

        (await CommentsAsync(s.Reporter, s.IncidentId)).Single().GetProperty("attachments").GetArrayLength().Should().Be(1);
        (await s.Reporter.GetAsync($"/api/v1/attachments/{file.Id}/download")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Comment_AttachmentLinkedConcurrently_OneWins()
    {
        var s = await SeedScenarioAsync();
        var file = await SeedAsync(factory, s.ResponderId, ScanStatus.Clean);

        var responses = await Task.WhenAll(
            PostCommentAsync(s.Responder, s.IncidentId, [file.Id]),
            PostCommentAsync(s.Responder, s.IncidentId, [file.Id]));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(
            new[] { HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity });

        // The loser's comment, history and outbox rows rolled back with its failed link.
        (await CommentsAsync(s.Responder, s.IncidentId)).Should().ContainSingle();
        (await HistoryAsync(factory, s.IncidentId)).Count(h => h.Field == Incident.AttachmentHistoryField).Should().Be(1);
    }

    [Fact]
    public async Task Detail_ListsIncidentAttachmentsOnly()
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var userId = await factory.EnsureUserAsync(oid);
        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);
        var reported = await SeedAsync(factory, userId, ScanStatus.Clean, fileName: "report.png");
        var onComment = await SeedAsync(factory, userId, ScanStatus.Clean, fileName: "comment.png");
        var created = await JsonAsync(await client.PostAsJsonAsync("/api/v1/incidents", new
        {
            applicationId = appId,
            environment = "Production",
            severity = "High",
            title = $"Checkout fails {Guid.NewGuid():N}",
            description = "<p>Error.</p>",
            attachmentIds = new[] { reported.Id },
        }));
        var incidentId = created.GetProperty("id").GetGuid();
        (await PostCommentAsync(client, incidentId, [onComment.Id])).StatusCode.Should().Be(HttpStatusCode.Created);

        var detail = await JsonAsync(await client.GetAsync($"/api/v1/incidents/{created.GetProperty("number").GetString()}"));

        detail.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()).Should().Equal("report.png");
    }

    [Fact]
    public async Task Comments_List_UsesConstantQueryCount()
    {
        var one = await SeedCommentsWithFilesAsync(1);
        var many = await SeedCommentsWithFilesAsync(10);

        var queriesForOne = await CountQueriesAsync(one);
        var queriesForMany = await CountQueriesAsync(many);

        queriesForMany.Should().Be(queriesForOne);
    }

    private async Task<Guid> SeedCommentsWithFilesAsync(int comments)
    {
        var s = await SeedScenarioAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var incident = await db.Incidents.SingleAsync(i => i.Id == s.IncidentId);
        var actor = new Actor(s.ResponderId, new HashSet<UserRole> { UserRole.Responder });
        for (var i = 0; i < comments; i++)
        {
            var file = Attachment.Create(
                Guid.NewGuid(), s.ResponderId, $"f{i}.png", "image/png", 10, $"seed/{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
            db.Comments.Add(incident.AddComment($"<p>c{i}</p>", false, actor, DateTimeOffset.UtcNow.AddSeconds(i), [file]));
            db.Attachments.Add(file);
        }

        await db.SaveChangesAsync();
        return s.IncidentId;
    }

    private async Task<int> CountQueriesAsync(Guid incidentId)
    {
        var counter = new CommandCounter();
        using var scope = factory.Services.CreateScope();
        var options = new DbContextOptionsBuilder<AppDbContext>(scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>())
            .AddInterceptors(counter)
            .Options;
        await using var db = new AppDbContext(options);

        var user = Substitute.For<ICurrentUser>();
        var userId = Guid.NewGuid();
        user.UserId.Returns(userId);
        user.ToActor().Returns(new Actor(userId, new HashSet<UserRole> { UserRole.Responder }));
        var teams = Substitute.For<IAppTeamReader>();
        teams.IsMemberAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var page = await new ListCommentsHandler(db, user, teams)
            .Handle(new ListCommentsQuery(incidentId, null, 25, false), CancellationToken.None);

        page.Items.Should().OnlyContain(c => c.Attachments.Count == 1);
        return counter.Count;
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => _count;

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
