using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Persistence.Search;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Persistence;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlSimilarIncidentSearchTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private async Task<IReadOnlyList<Guid>> SearchAsync(Guid appId, string text, int limit = 5)
    {
        using var scope = factory.Services.CreateScope();
        var search = new SqlSimilarIncidentSearch(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        return await search.FindOpenAsync(appId, text, limit, CancellationToken.None);
    }

    private static string Token() => $"tok{Guid.NewGuid():N}";

    // Waits for the control incident, so a negative assertion on the other rows is not vacuous.
    private Task WaitUntilFoundAsync(Guid appId, string text, Guid expected) =>
        IncidentSeeder.EventuallyAsync(async () => (await SearchAsync(appId, text)).Contains(expected));

    [Fact]
    public async Task FindOpenAsync_ClosedMatch_Excluded()
    {
        var appId = await factory.CreateAppAsync();
        var token = Token();
        var open = await _seeder.AddAsync(appId, $"Checkout {token} broken");
        var closed = await _seeder.AddAsync(appId, $"Checkout {token} crashed", closed: true);
        await _seeder.WaitForFullTextPopulationAsync(TimeSpan.FromSeconds(45));
        await WaitUntilFoundAsync(appId, token, open);

        var result = await SearchAsync(appId, token);

        result.Should().Equal(open);
        result.Should().NotContain(closed);
    }

    [Fact]
    public async Task FindOpenAsync_OtherApplicationMatch_Excluded()
    {
        var appId = await factory.CreateAppAsync();
        var otherAppId = await factory.CreateAppAsync();
        var token = Token();
        var mine = await _seeder.AddAsync(appId, $"Checkout {token} broken");
        await _seeder.AddAsync(otherAppId, $"Checkout {token} broken");
        await _seeder.WaitForFullTextPopulationAsync(TimeSpan.FromSeconds(45));
        await WaitUntilFoundAsync(appId, token, mine);

        var result = await SearchAsync(appId, token);

        result.Should().Equal(mine);
    }

    [Fact]
    public async Task FindOpenAsync_MoreThanLimit_ReturnsOnlyTheLimit()
    {
        var appId = await factory.CreateAppAsync();
        var token = Token();
        var created = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            created.Add(await _seeder.AddAsync(appId, $"Payment {token} gateway {Guid.NewGuid():N}"));
        }

        await _seeder.WaitForFullTextPopulationAsync(TimeSpan.FromSeconds(45));
        await IncidentSeeder.EventuallyAsync(async () => (await SearchAsync(appId, token, limit: 8)).Count == 8);

        var result = await SearchAsync(appId, token, limit: 5);

        result.Should().HaveCount(5);
        result.Should().OnlyContain(id => created.Contains(id));
        result.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task FindOpenAsync_InflectedWord_Matches()
    {
        var appId = await factory.CreateAppAsync();
        var token = Token();
        var id = await _seeder.AddAsync(appId, $"{token} checkout failing after payment");

        await WaitUntilFoundAsync(appId, $"{token} checkout fails", id);
    }

    [Fact]
    public async Task FindOpenAsync_DescriptionOnlyMatch_NotReturned()
    {
        var appId = await factory.CreateAppAsync();
        var token = Token();
        var control = await _seeder.AddAsync(appId, $"Control {token}");
        await _seeder.WaitForFullTextPopulationAsync(TimeSpan.FromSeconds(45));
        await WaitUntilFoundAsync(appId, token, control);

        // The seeded description ("Users see an error page") is not part of any title.
        var result = await SearchAsync(appId, "error page");

        result.Should().NotContain(control);
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("*")]
    [InlineData("(")]
    [InlineData("AND")]
    [InlineData("NEAR")]
    [InlineData("'; DROP TABLE Incidents; --")]
    [InlineData("\"unterminated phrase")]
    public async Task FindOpenAsync_FullTextOperatorsInInput_DoesNotThrow(string text)
    {
        var appId = await factory.CreateAppAsync();

        var act = () => SearchAsync(appId, text);

        await act.Should().NotThrowAsync();
    }
}
