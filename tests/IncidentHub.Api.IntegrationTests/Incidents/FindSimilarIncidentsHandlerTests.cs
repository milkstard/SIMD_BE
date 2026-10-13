using FluentAssertions;
using FluentValidation;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Incidents.FindSimilarIncidents;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace IncidentHub.Api.IntegrationTests.Incidents;

/// <summary>Handler against the real SQL container; team reader, current user and the full-text search are fakes.</summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FindSimilarIncidentsHandlerTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);
    private readonly IAppTeamReader _teams = Substitute.For<IAppTeamReader>();
    private readonly ISimilarIncidentSearch _search = Substitute.For<ISimilarIncidentSearch>();

    private async Task<(IServiceScope Scope, FindSimilarIncidentsHandler Handler, Guid AppId)> ArrangeAsync(bool isMember = true, bool isActive = true)
    {
        var appId = await factory.CreateAppAsync();
        var userId = await factory.EnsureUserAsync(Guid.NewGuid().ToString());

        var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!isActive)
        {
            var app = await db.Applications.SingleAsync(a => a.Id == appId);
            app.Update(app.Name, app.Code, app.OwningTeamId, null, app.Environments, isActive: false);
            await db.SaveChangesAsync();
        }

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.ToActor().Returns(new Actor(userId, new HashSet<UserRole> { UserRole.Responder }));
        _teams.IsMemberAsync(userId, appId, Arg.Any<CancellationToken>()).Returns(isMember);

        return (scope, new FindSimilarIncidentsHandler(db, user, _teams, _search), appId);
    }

    private static async Task<string> ErrorKeyAsync(Func<Task> act) =>
        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Single().PropertyName;

    [Fact]
    public async Task Handle_NotMember_ThrowsValidationOnApplicationId()
    {
        var (scope, handler, appId) = await ArrangeAsync(isMember: false);
        using var _ = scope;

        var key = await ErrorKeyAsync(() => handler.Handle(new FindSimilarIncidentsQuery(appId, "Checkout fails"), CancellationToken.None));

        key.Should().Be("ApplicationId");
        await _search.DidNotReceiveWithAnyArgs().FindOpenAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Handle_InactiveApplication_ThrowsValidationOnApplicationId()
    {
        var (scope, handler, appId) = await ArrangeAsync(isActive: false);
        using var _ = scope;

        var key = await ErrorKeyAsync(() => handler.Handle(new FindSimilarIncidentsQuery(appId, "Checkout fails"), CancellationToken.None));

        key.Should().Be("ApplicationId");
    }

    [Fact]
    public async Task Handle_UnknownApplication_ThrowsValidationOnApplicationId()
    {
        var (scope, handler, _) = await ArrangeAsync();
        using var _ = scope;

        var key = await ErrorKeyAsync(() => handler.Handle(new FindSimilarIncidentsQuery(Guid.NewGuid(), "Checkout fails"), CancellationToken.None));

        key.Should().Be("ApplicationId");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("  a  ")]
    public async Task Handle_TitleShorterThanThree_ReturnsEmptyWithoutSearch(string title)
    {
        var (scope, handler, appId) = await ArrangeAsync();
        using var _ = scope;

        var result = await handler.Handle(new FindSimilarIncidentsQuery(appId, title), CancellationToken.None);

        result.Should().BeEmpty();
        await _search.DidNotReceiveWithAnyArgs().FindOpenAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Handle_NoMatches_ReturnsEmpty()
    {
        var (scope, handler, appId) = await ArrangeAsync();
        using var _ = scope;
        _search.FindOpenAsync(appId, "Checkout fails", FindSimilarIncidentsHandler.MaxResults, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Guid>());

        var result = await handler.Handle(new FindSimilarIncidentsQuery(appId, " Checkout fails "), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Matches_ReturnsSummariesInRankOrder()
    {
        var (scope, handler, appId) = await ArrangeAsync();
        using var _ = scope;
        var first = await _seeder.AddAsync(appId, $"Rank one {Guid.NewGuid():N}");
        var second = await _seeder.AddAsync(appId, $"Rank two {Guid.NewGuid():N}");
        var third = await _seeder.AddAsync(appId, $"Rank three {Guid.NewGuid():N}");
        _search.FindOpenAsync(appId, "Rank", FindSimilarIncidentsHandler.MaxResults, Arg.Any<CancellationToken>())
            .Returns(new[] { third, first, second });

        var result = await handler.Handle(new FindSimilarIncidentsQuery(appId, "Rank"), CancellationToken.None);

        result.Select(r => r.Id).Should().Equal(third, first, second);
        result[0].Application.Id.Should().Be(appId);
        result[0].Number.Should().StartWith("INC-");
        result[0].Reporter.Email.Should().EndWith("@test.local");
        result[0].Assignee.Should().BeNull();
        result[0].AssignedTeam.Should().BeNull();
        result[0].CommentCount.Should().Be(0);
        result[0].RowVersion.Should().NotBeNullOrWhiteSpace();
    }
}
