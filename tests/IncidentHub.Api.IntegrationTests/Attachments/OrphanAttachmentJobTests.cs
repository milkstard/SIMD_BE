using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Infrastructure.Attachments;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static IncidentHub.Api.IntegrationTests.Attachments.AttachmentTestSupport;

namespace IncidentHub.Api.IntegrationTests.Attachments;

/// <summary>
/// The rows are Clean on purpose: a leftover Pending row older than an hour would trip the scan job's stuck-attachment warning
/// in the scan tests that run after these.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OrphanAttachmentJobTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private async Task<int> SweepAsync(IFileStore? store = null)
    {
        using var scope = factory.Services.CreateScope();
        var sweeper = new OrphanAttachmentSweeper(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            store ?? scope.ServiceProvider.GetRequiredService<IFileStore>(),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<OrphanAttachmentSweeper>.Instance);
        return await sweeper.RunOnceAsync(CancellationToken.None);
    }

    private async Task<bool> RowExistsAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Attachments.AnyAsync(a => a.Id == id);
    }

    private static async Task<bool> BlobExistsAsync(ApiFactory factory, string path) =>
        await Container(factory).GetBlobClient(path).ExistsAsync();

    private async Task<Attachment> OldUploadAsync(Guid uploaderId, TimeSpan age) =>
        await SeedAsync(factory, uploaderId, ScanStatus.Clean, createdAt: DateTimeOffset.UtcNow - age);

    [Fact]
    public async Task Sweep_OldUnlinked_DeletesRowAndBlob()
    {
        var uploaderId = await factory.EnsureUserAsync(NewOid());
        var old = await OldUploadAsync(uploaderId, TimeSpan.FromHours(25));
        (await BlobExistsAsync(factory, old.BlobPath)).Should().BeTrue();

        var removed = await SweepAsync();

        removed.Should().BeGreaterThanOrEqualTo(1);
        (await RowExistsAsync(old.Id)).Should().BeFalse();
        (await BlobExistsAsync(factory, old.BlobPath)).Should().BeFalse();
    }

    [Fact]
    public async Task Sweep_Linked_Untouched()
    {
        var uploaderId = await factory.EnsureUserAsync(NewOid());
        var appId = await factory.CreateAppAsync();
        var (incidentId, _) = await _seeder.AddIncidentAsync(appId, $"Checkout fails {Guid.NewGuid():N}");
        var old = await OldUploadAsync(uploaderId, TimeSpan.FromHours(48));
        await LinkAsync(factory, old.Id, incidentId);

        await SweepAsync();

        (await RowExistsAsync(old.Id)).Should().BeTrue();
        (await BlobExistsAsync(factory, old.BlobPath)).Should().BeTrue();
    }

    [Fact]
    public async Task Sweep_Recent_Untouched()
    {
        var uploaderId = await factory.EnsureUserAsync(NewOid());
        var recent = await OldUploadAsync(uploaderId, TimeSpan.FromHours(23));

        await SweepAsync();

        (await RowExistsAsync(recent.Id)).Should().BeTrue();
        (await BlobExistsAsync(factory, recent.BlobPath)).Should().BeTrue();
    }

    [Fact]
    public async Task Sweep_BlobDeleteFails_StillRemovesTheRowAndKeepsGoing()
    {
        var uploaderId = await factory.EnsureUserAsync(NewOid());
        var old = await OldUploadAsync(uploaderId, TimeSpan.FromHours(30));
        var failing = Substitute.For<IFileStore>();
        failing.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("storage down"));

        var removed = await SweepAsync(failing);

        removed.Should().BeGreaterThanOrEqualTo(1);
        (await RowExistsAsync(old.Id)).Should().BeFalse();
    }
}
