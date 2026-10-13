using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Attachments;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RedisDistributedLockTests(ApiFactory factory)
{
    private IDistributedLock Locks => factory.Services.GetRequiredService<IDistributedLock>();

    private static string NewName() => $"test-{Guid.NewGuid():N}";

    [Fact]
    public async Task TryAcquire_WhileHeld_ReturnsNull()
    {
        var name = NewName();

        await using var first = await Locks.TryAcquireAsync(name, TimeSpan.FromSeconds(30), CancellationToken.None);
        var second = await Locks.TryAcquireAsync(name, TimeSpan.FromSeconds(30), CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquire_AfterRelease_Succeeds()
    {
        var name = NewName();
        var first = await Locks.TryAcquireAsync(name, TimeSpan.FromSeconds(30), CancellationToken.None);
        await first!.DisposeAsync();

        await using var second = await Locks.TryAcquireAsync(name, TimeSpan.FromSeconds(30), CancellationToken.None);

        second.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_DifferentNames_DoNotBlockEachOther()
    {
        await using var first = await Locks.TryAcquireAsync(NewName(), TimeSpan.FromSeconds(30), CancellationToken.None);
        await using var second = await Locks.TryAcquireAsync(NewName(), TimeSpan.FromSeconds(30), CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_AfterTtlExpires_Succeeds()
    {
        var name = NewName();
        await Locks.TryAcquireAsync(name, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(700));

        await using var second = await Locks.TryAcquireAsync(name, TimeSpan.FromSeconds(30), CancellationToken.None);

        second.Should().NotBeNull();
    }

    [Fact]
    public async Task Dispose_ByAnExpiredHolder_DoesNotReleaseTheNextHoldersLock()
    {
        var name = NewName();
        var expired = await Locks.TryAcquireAsync(name, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        await using var current = await Locks.TryAcquireAsync(name, TimeSpan.FromSeconds(30), CancellationToken.None);

        await expired!.DisposeAsync();

        (await Locks.TryAcquireAsync(name, TimeSpan.FromSeconds(30), CancellationToken.None)).Should().BeNull();
    }
}
