using System.Net.Http.Headers;
using Azure.Storage.Blobs;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Attachments;

/// <summary>Plumbing shared by the attachment tests: multipart bodies, seeded attachments and blob access.</summary>
internal static class AttachmentTestSupport
{
    public const string ContainerName = "attachments";

    public static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1];

    public const string Eicar = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    public static string NewOid() => Guid.NewGuid().ToString();

    /// <summary>A client that does not follow redirects, so the 302 can be inspected.</summary>
    public static HttpClient NewClient(ApiFactory factory, string oid, params UserRole[] roles) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).AsUser(factory, oid, roles);

    public static MultipartFormDataContent FileBody(byte[] content, string fileName, string contentType, string partName = "file")
    {
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { part, partName, fileName } };
    }

    public static Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string fileName, string contentType) =>
        client.PostAsync("/api/v1/attachments", FileBody(content, fileName, contentType));

    public static BlobContainerClient Container(ApiFactory factory) =>
        new BlobServiceClient(factory.BlobConnectionString).GetBlobContainerClient(ContainerName);

    public static async Task<Attachment> LoadAsync(ApiFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Attachments.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    public static async Task<int> CountForUploaderAsync(ApiFactory factory, Guid uploaderId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Attachments.CountAsync(a => a.UploadedById == uploaderId);
    }

    /// <summary>Stores a real blob and its row, as the upload endpoint would, with the given scan result.</summary>
    public static async Task<Attachment> SeedAsync(
        ApiFactory factory,
        Guid uploaderId,
        ScanStatus status,
        byte[]? content = null,
        string fileName = "shot.png",
        string contentType = "image/png",
        DateTimeOffset? createdAt = null)
    {
        content ??= Png;
        var id = Guid.NewGuid();
        var path = $"attachments/seed/{id:N}";

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IFileStore>()
            .UploadAsync(path, new MemoryStream(content), contentType, CancellationToken.None);

        var attachment = Attachment.Create(id, uploaderId, fileName, contentType, content.Length, path, createdAt ?? DateTimeOffset.UtcNow);
        if (status != ScanStatus.Pending)
        {
            attachment.MarkScanned(status);
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync();
        return attachment;
    }

    /// <summary>Links an attachment the way spec 08_03 will; the linking operation does not exist yet.</summary>
    public static async Task LinkAsync(ApiFactory factory, Guid attachmentId, Guid incidentId, Guid? commentId = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Attachments SET IncidentId = {incidentId}, CommentId = {commentId} WHERE Id = {attachmentId}");
    }

    /// <summary>Adds a comment to the incident through the domain and returns its id.</summary>
    public static async Task<Guid> AddCommentAsync(ApiFactory factory, Guid incidentId, Guid authorId, bool isInternal)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var incident = await db.Incidents.SingleAsync(i => i.Id == incidentId);
        var comment = incident.AddComment(
            "<p>Seeded</p>",
            isInternal,
            new Actor(authorId, new HashSet<UserRole> { UserRole.Responder }),
            DateTimeOffset.UtcNow);
        db.Comments.Add(comment);
        await db.SaveChangesAsync();
        return comment.Id;
    }

    /// <summary>Adds Pending rows that point at blobs which do not exist, created at <paramref name="createdAt"/>.</summary>
    public static async Task<List<Guid>> SeedMissingBlobRowsAsync(
        ApiFactory factory,
        Guid uploaderId,
        int count,
        DateTimeOffset createdAt,
        string pathPrefix)
    {
        var ids = new List<Guid>();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            db.Attachments.Add(Attachment.Create(id, uploaderId, "gone.png", "image/png", 10, $"{pathPrefix}/{id:N}", createdAt));
            ids.Add(id);
        }

        await db.SaveChangesAsync();
        return ids;
    }

    public static async Task RemoveAsync(ApiFactory factory, IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Attachments.Where(a => list.Contains(a.Id)).ExecuteDeleteAsync();
    }
}

/// <summary>Collects what a component logs, to assert on levels and messages.</summary>
internal sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    private readonly List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
