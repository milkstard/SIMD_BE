using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Infrastructure.Persistence.Configurations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, PostCommitHook? postCommit = null) : DbContext(options), IAppDbContext
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public DbSet<User> Users => Set<User>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<MonitoredApp> Applications => Set<MonitoredApp>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<IncidentHistory> IncidentHistory => Set<IncidentHistory>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<SlaPolicySetting> SlaPolicies => Set<SlaPolicySetting>();

    /// <summary>Written only by this context (see <see cref="StageOutbox"/>); deliberately not on <see cref="IAppDbContext"/>.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Rejects edits to the append-only history, turns the domain events of every tracked aggregate into
    /// <see cref="OutboxMessage"/> rows saved in the same transaction, then reports unique-index violations without leaking SQL types:
    /// the open-fingerprint index becomes <see cref="DuplicateIncidentException"/> (a <see cref="ConflictException"/>),
    /// any other one <see cref="DuplicateKeyException"/> (still a <see cref="DbUpdateException"/>). Handlers turn both into a 409.
    /// The events are cleared from the aggregates only once the save succeeded, and only then handed to the <see cref="PostCommitHook"/>
    /// (never mid-transaction, and nothing is dispatched for a failed save).
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var staged = BeforeSave();

        try
        {
            var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            OnSaved(staged);
            await DispatchAsync(staged);
            return saved;
        }
        catch (DbUpdateException ex) when (TranslateUniqueViolation(ex) is { } translated)
        {
            DetachOutbox(staged);
            throw translated;
        }
        catch
        {
            DetachOutbox(staged);
            throw;
        }
    }

    /// <summary>Synchronous twin of <see cref="SaveChangesAsync(bool, CancellationToken)"/> with identical behaviour.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var staged = BeforeSave();

        try
        {
            var saved = base.SaveChanges(acceptAllChangesOnSuccess);
            OnSaved(staged);
            DispatchAsync(staged).GetAwaiter().GetResult();
            return saved;
        }
        catch (DbUpdateException ex) when (TranslateUniqueViolation(ex) is { } translated)
        {
            DetachOutbox(staged);
            throw translated;
        }
        catch
        {
            DetachOutbox(staged);
            throw;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasIncidentNumberSequence();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    private StagedOutbox BeforeSave()
    {
        EnsureHistoryIsAppendOnly();
        return StageOutbox();
    }

    /// <summary>Maps a unique-index violation to its domain/application exception; null for any other failure.</summary>
    private static Exception? TranslateUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is not SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql)
        {
            return null;
        }

        return sql.Message.Contains(IncidentConfiguration.FingerprintIndexName, StringComparison.Ordinal)
            ? new DuplicateIncidentException()
            : new DuplicateKeyException("A record with the same unique value already exists.", sql);
    }

    private void EnsureHistoryIsAppendOnly()
    {
        var tampered = ChangeTracker.Entries<IncidentHistory>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (tampered)
        {
            throw new InvalidOperationException("IncidentHistory is append-only; rows cannot be modified or deleted.");
        }
    }

    /// <summary>Adds one outbox row per pending domain event so they are saved with the change that raised them.</summary>
    private StagedOutbox StageOutbox()
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        var messages = aggregates
            .SelectMany(a => a.DomainEvents)
            .Select(OutboxMessage.From)
            .ToList();

        OutboxMessages.AddRange(messages);
        return new StagedOutbox(aggregates, messages, aggregates.SelectMany(a => a.DomainEvents).ToList());
    }

    private static void OnSaved(StagedOutbox staged)
    {
        foreach (var aggregate in staged.Aggregates)
        {
            aggregate.ClearDomainEvents();
        }
    }

    private Task DispatchAsync(StagedOutbox staged) =>
        postCommit is null ? Task.CompletedTask : postCommit.RunAsync(staged.Events);

    /// <summary>After a failed save the aggregates keep their events, so the outbox rows must not stay tracked or a retry would add them twice.</summary>
    private void DetachOutbox(StagedOutbox staged)
    {
        foreach (var message in staged.Messages)
        {
            Entry(message).State = EntityState.Detached;
        }
    }

    /// <param name="Events">Snapshot taken before the aggregates are cleared, in the order they were raised.</param>
    private sealed record StagedOutbox(IReadOnlyList<AggregateRoot> Aggregates, IReadOnlyList<OutboxMessage> Messages, IReadOnlyList<IDomainEvent> Events);
}
