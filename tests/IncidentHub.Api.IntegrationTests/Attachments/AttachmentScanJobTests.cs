using System.Text;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Infrastructure.Attachments;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static IncidentHub.Api.IntegrationTests.Attachments.AttachmentTestSupport;

namespace IncidentHub.Api.IntegrationTests.Attachments;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AttachmentScanJobTests(ApiFactory factory)
{
    private async Task<Guid> UploaderAsync() => await factory.EnsureUserAsync(NewOid());

    /// <summary>One pass of the scan job with the real store and scanner, as the Worker runs it.</summary>
    private async Task<int> RunOnceAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AttachmentScanRunner>().RunOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Scan_CleanFile_MarksClean()
    {
        var attachment = await SeedAsync(factory, await UploaderAsync(), ScanStatus.Pending);

        var scanned = await RunOnceAsync();

        scanned.Should().BeGreaterThanOrEqualTo(1);
        (await LoadAsync(factory, attachment.Id)).ScanStatus.Should().Be(ScanStatus.Clean);
    }

    [Fact]
    public async Task Scan_EicarFile_MarksInfected()
    {
        var attachment = await SeedAsync(
            factory,
            await UploaderAsync(),
            ScanStatus.Pending,
            Encoding.ASCII.GetBytes(Eicar),
            "eicar.txt",
            "text/plain");

        await RunOnceAsync();

        (await LoadAsync(factory, attachment.Id)).ScanStatus.Should().Be(ScanStatus.Infected);
    }

    [Fact]
    public async Task Scan_InfectedFile_StaysUndownloadable()
    {
        var oid = NewOid();
        var uploaderId = await factory.EnsureUserAsync(oid);
        var attachment = await SeedAsync(factory, uploaderId, ScanStatus.Pending, Encoding.ASCII.GetBytes(Eicar), "eicar.txt", "text/plain");
        await RunOnceAsync();

        var response = await NewClient(factory, oid, IncidentHub.Domain.Users.UserRole.Responder)
            .GetAsync($"/api/v1/attachments/{attachment.Id}/download");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Scan_AlreadyScannedRows_AreLeftAlone()
    {
        var attachment = await SeedAsync(factory, await UploaderAsync(), ScanStatus.Infected);

        await RunOnceAsync();

        (await LoadAsync(factory, attachment.Id)).ScanStatus.Should().Be(ScanStatus.Infected);
    }

    [Fact]
    public async Task Scan_StoreFailure_LeavesPending()
    {
        var attachment = await SeedAsync(factory, await UploaderAsync(), ScanStatus.Pending);
        var failingStore = Substitute.For<IFileStore>();
        failingStore.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("storage down"));

        using var scope = factory.Services.CreateScope();
        var runner = new AttachmentScanRunner(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            failingStore,
            new StubAttachmentScanner(),
            TimeProvider.System,
            NullLogger<AttachmentScanRunner>.Instance);

        var scanned = await runner.RunOnceAsync(CancellationToken.None);

        scanned.Should().Be(0);
        var row = await LoadAsync(factory, attachment.Id);
        row.ScanStatus.Should().Be(ScanStatus.Pending);
        row.ScanAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Scan_ScannerFailure_LeavesPending()
    {
        var attachment = await SeedAsync(factory, await UploaderAsync(), ScanStatus.Pending);
        var failingScanner = Substitute.For<IAttachmentScanner>();
        failingScanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("engine down"));

        using var scope = factory.Services.CreateScope();
        var runner = new AttachmentScanRunner(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IFileStore>(),
            failingScanner,
            TimeProvider.System,
            NullLogger<AttachmentScanRunner>.Instance);

        await runner.RunOnceAsync(CancellationToken.None);

        var row = await LoadAsync(factory, attachment.Id);
        row.ScanStatus.Should().Be(ScanStatus.Pending);
        row.ScanAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Scan_RowsThatKeepFailing_DoNotStarveNewerUploads()
    {
        var uploaderId = await UploaderAsync();
        var broken = await SeedMissingBlobRowsAsync(factory, uploaderId, BatchSizeWithMargin, DateTimeOffset.UtcNow.AddMinutes(-30), "missing/starve");
        try
        {
            // Every broken row fails once, so all of them rank behind a row that has not failed yet.
            await RunWithStoreAsync(new MissingBlobStore(factory, "missing/"));
            await RunWithStoreAsync(new MissingBlobStore(factory, "missing/"));
            var fresh = await SeedAsync(factory, uploaderId, ScanStatus.Pending);

            await RunWithStoreAsync(new MissingBlobStore(factory, "missing/"));

            (await LoadAsync(factory, fresh.Id)).ScanStatus.Should().Be(ScanStatus.Clean);
            (await LoadAsync(factory, broken[0])).ScanAttempts.Should().BeGreaterThan(0);
        }
        finally
        {
            await RemoveAsync(factory, broken);
        }
    }

    [Fact]
    public async Task Scan_OldestPendingOverOneHour_LogsWarningEvenWhenNothingThrows()
    {
        var uploaderId = await UploaderAsync();
        var stuck = await SeedMissingBlobRowsAsync(factory, uploaderId, 1, DateTimeOffset.UtcNow.AddHours(-2), "missing/stuck");
        try
        {
            // The batch never reaches the stuck row's blob here: the store is healthy and the row is simply old.
            var logger = new ListLogger<AttachmentScanRunner>();

            await RunWithStoreAsync(factory.Services.GetRequiredService<IFileStore>(), logger);

            logger.Entries.Should().Contain(e =>
                e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains(stuck[0].ToString()));
        }
        finally
        {
            await RemoveAsync(factory, stuck);
        }
    }

    [Fact]
    public async Task Scan_BudgetSpent_StartsNoFileAndLeavesRowsUntouched()
    {
        var attachment = await SeedAsync(factory, await UploaderAsync(), ScanStatus.Pending);

        using var scope = factory.Services.CreateScope();
        var scanned = await scope.ServiceProvider.GetRequiredService<AttachmentScanRunner>()
            .RunOnceAsync(CancellationToken.None, TimeSpan.Zero);

        scanned.Should().Be(0);
        var row = await LoadAsync(factory, attachment.Id);
        row.ScanStatus.Should().Be(ScanStatus.Pending);
        row.ScanAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Scan_RecentPending_LogsNoWarning()
    {
        await SeedAsync(factory, await UploaderAsync(), ScanStatus.Pending);
        var logger = new ListLogger<AttachmentScanRunner>();

        await RunWithStoreAsync(factory.Services.GetRequiredService<IFileStore>(), logger);

        logger.Entries.Should().NotContain(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    private const int BatchSizeWithMargin = AttachmentScanRunner.BatchSize + 5;

    private async Task RunWithStoreAsync(IFileStore store, Microsoft.Extensions.Logging.ILogger<AttachmentScanRunner>? logger = null)
    {
        using var scope = factory.Services.CreateScope();
        var runner = new AttachmentScanRunner(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            store,
            new StubAttachmentScanner(),
            TimeProvider.System,
            logger ?? NullLogger<AttachmentScanRunner>.Instance);
        await runner.RunOnceAsync(CancellationToken.None);
    }

    /// <summary>Real store, except that blobs under a path prefix are "missing".</summary>
    private sealed class MissingBlobStore(ApiFactory factory, string missingPrefix) : IFileStore
    {
        private IFileStore Inner => factory.Services.GetRequiredService<IFileStore>();

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken) =>
            path.StartsWith(missingPrefix, StringComparison.Ordinal)
                ? throw new IOException("blob is gone")
                : Inner.OpenReadAsync(path, cancellationToken);

        public Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken) =>
            Inner.UploadAsync(path, content, contentType, cancellationToken);

        public Task DeleteAsync(string path, CancellationToken cancellationToken) => Inner.DeleteAsync(path, cancellationToken);

        public Task<Uri> CreateDownloadUriAsync(string path, string fileName, string contentType, TimeSpan lifetime, CancellationToken cancellationToken) =>
            Inner.CreateDownloadUriAsync(path, fileName, contentType, lifetime, cancellationToken);
    }
}
