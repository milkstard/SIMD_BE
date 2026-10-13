using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Persistence;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlIncidentTextSearchTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private async Task<IReadOnlyList<Guid>> FindAsync(string text, params Guid[] appIds)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IIncidentTextSearch>()
            .FindIdsAsync(text, appIds, 500, CancellationToken.None);
    }

    private async Task<IReadOnlyList<Guid>> FindEventuallyAsync(string text, int expected, params Guid[] appIds)
    {
        IReadOnlyList<Guid> found = [];
        await IncidentSeeder.EventuallyAsync(async () =>
        {
            found = await FindAsync(text, appIds);
            return found.Count >= expected;
        });
        return found;
    }

    [Fact]
    public async Task FindIds_TitleMatch_Returns()
    {
        var appId = await factory.CreateAppAsync();
        var token = $"tok{Guid.NewGuid():N}";
        var id = await _seeder.AddAsync(appId, $"Gateway {token} down");

        (await FindEventuallyAsync(token, 1, appId)).Should().Equal(id);
    }

    [Fact]
    public async Task FindIds_DescriptionMatch_Returns()
    {
        var appId = await factory.CreateAppAsync();
        var token = $"tok{Guid.NewGuid():N}";
        var (id, _) = await _seeder.AddIncidentAsync(appId, "Nothing in the title", description: $"<p>Stack {token}</p>");

        (await FindEventuallyAsync(token, 1, appId)).Should().Equal(id);
    }

    [Fact]
    public async Task FindIds_OutsideApplicationIds_Excluded()
    {
        var appId = await factory.CreateAppAsync();
        var otherApp = await factory.CreateAppAsync();
        var token = $"tok{Guid.NewGuid():N}";
        var inside = await _seeder.AddAsync(appId, $"Inside {token}");
        await _seeder.AddAsync(otherApp, $"Outside {token}");
        await _seeder.WaitForFullTextPopulationAsync(TimeSpan.FromSeconds(45));

        (await FindEventuallyAsync(token, 1, appId)).Should().Equal(inside);
    }

    [Fact]
    public async Task FindIds_NoApplicationIds_ReturnsEmpty()
    {
        var appId = await factory.CreateAppAsync();
        var token = $"tok{Guid.NewGuid():N}";
        await _seeder.AddAsync(appId, $"Anything {token}");
        await _seeder.WaitForFullTextPopulationAsync(TimeSpan.FromSeconds(45));

        (await FindAsync(token)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("*")]
    [InlineData("(")]
    [InlineData("AND")]
    [InlineData("NEAR")]
    [InlineData("'; DROP TABLE Incidents; --")]
    public async Task FindIds_OperatorsInInput_DoesNotThrow(string text)
    {
        var appId = await factory.CreateAppAsync();

        var act = () => FindAsync(text, appId);

        await act.Should().NotThrowAsync();
    }
}
