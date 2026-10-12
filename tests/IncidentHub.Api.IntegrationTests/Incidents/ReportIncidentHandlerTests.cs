using FluentAssertions;
using FluentValidation;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Application.Incidents.ReportIncident;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Severity = IncidentHub.Domain.Incidents.Severity;

namespace IncidentHub.Api.IntegrationTests.Incidents;

/// <summary>Handler against the real SQL container; team reader, sanitizer, SLA provider and clock are fakes.</summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReportIncidentHandlerTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly IAppTeamReader _teams = Substitute.For<IAppTeamReader>();
    private readonly IHtmlSanitizer _sanitizer = Substitute.For<IHtmlSanitizer>();
    private readonly ISlaPolicyProvider _sla = Substitute.For<ISlaPolicyProvider>();
    private readonly FakeTimeProvider _time = new(Now);

    private async Task<(IServiceScope Scope, ReportIncidentHandler Handler, Guid AppId)> ArrangeAsync(bool isMember = true, bool isActive = true)
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
        _sanitizer.Sanitize(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _sla.GetAsync(Arg.Any<Severity>(), Arg.Any<CancellationToken>())
            .Returns(call => new SlaPolicy(call.Arg<Severity>(), 10, 20, 80));

        var handler = new ReportIncidentHandler(db, user, _teams, _sanitizer, _sla, _time, NullLogger<ReportIncidentHandler>.Instance);
        return (scope, handler, appId);
    }

    private static ReportIncidentCommand Command(
        Guid appId,
        string? title = null,
        AppEnvironment environment = AppEnvironment.Production,
        string description = "<p>It broke</p>") =>
        new(appId, environment, Severity.High, title ?? $"Checkout broken {Guid.NewGuid():N}", description, null, []);

    private static async Task<string> ErrorKeyAsync(Func<Task> act) =>
        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Single().PropertyName;

    [Fact]
    public async Task Handle_NotMember_ThrowsValidationOnApplicationId()
    {
        var (scope, handler, appId) = await ArrangeAsync(isMember: false);
        using var _ = scope;

        var key = await ErrorKeyAsync(() => handler.Handle(Command(appId), CancellationToken.None));

        key.Should().Be("ApplicationId");
    }

    [Fact]
    public async Task Handle_InactiveApplication_ThrowsValidationOnApplicationId()
    {
        var (scope, handler, appId) = await ArrangeAsync(isActive: false);
        using var _ = scope;

        var key = await ErrorKeyAsync(() => handler.Handle(Command(appId), CancellationToken.None));

        key.Should().Be("ApplicationId");
    }

    [Fact]
    public async Task Handle_UnknownApplication_ThrowsValidationOnApplicationId()
    {
        var (scope, handler, _) = await ArrangeAsync();
        using var _ = scope;

        var key = await ErrorKeyAsync(() => handler.Handle(Command(Guid.NewGuid()), CancellationToken.None));

        key.Should().Be("ApplicationId");
    }

    [Fact]
    public async Task Handle_EnvironmentNotOffered_ThrowsValidationOnEnvironment()
    {
        var (scope, handler, appId) = await ArrangeAsync();
        using var _ = scope;

        var key = await ErrorKeyAsync(() => handler.Handle(Command(appId, environment: AppEnvironment.Development), CancellationToken.None));

        key.Should().Be("Environment");
    }

    [Fact]
    public async Task Handle_DescriptionEmptyAfterSanitize_ThrowsValidation()
    {
        var (scope, handler, appId) = await ArrangeAsync();
        using var _ = scope;
        _sanitizer.Sanitize(Arg.Any<string>()).Returns(" ");

        var key = await ErrorKeyAsync(() => handler.Handle(Command(appId, description: "<script>x</script>"), CancellationToken.None));

        key.Should().Be("Description");
    }

    [Fact]
    public async Task Handle_OpenDuplicate_ThrowsDuplicateWithNumber()
    {
        var (scope, handler, appId) = await ArrangeAsync();
        using var _ = scope;
        var title = $"Payment timeout {Guid.NewGuid():N}";
        var first = await handler.Handle(Command(appId, title), CancellationToken.None);

        var act = () => handler.Handle(Command(appId, title.ToUpperInvariant()), CancellationToken.None);

        (await act.Should().ThrowAsync<DuplicateIncidentException>()).Which.ExistingIncidentNumber.Should().Be(first.Number);
    }

    [Fact]
    public async Task Handle_Valid_SetsSlaDueTimesFromPolicy()
    {
        var (scope, handler, appId) = await ArrangeAsync();
        using var _ = scope;

        var result = await handler.Handle(Command(appId), CancellationToken.None);

        result.CreatedAt.Should().Be(Now);
        result.AckDueAt.Should().Be(Now.AddMinutes(10));
        result.ResolveDueAt.Should().Be(Now.AddMinutes(20));
        result.Status.Should().Be(IncidentStatus.New);
        result.Source.Should().Be(IncidentSource.Manual);
    }

    [Fact]
    public async Task Handle_Valid_StoresSanitizedDescriptionAndTrimmedTitle()
    {
        var (scope, handler, appId) = await ArrangeAsync();
        using var _ = scope;
        _sanitizer.Sanitize("<p>raw</p>").Returns("<p>clean</p>");
        var title = $"  Trimmed title {Guid.NewGuid():N}  ";

        var result = await handler.Handle(Command(appId, title, description: "<p>raw</p>"), CancellationToken.None);

        result.Description.Should().Be("<p>clean</p>");
        result.Title.Should().Be(title.Trim());
    }
}
