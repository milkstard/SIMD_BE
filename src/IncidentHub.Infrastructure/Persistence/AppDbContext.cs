using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence.Configurations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public DbSet<User> Users => Set<User>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<MonitoredApp> Applications => Set<MonitoredApp>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<IncidentHistory> IncidentHistory => Set<IncidentHistory>();

    /// <summary>
    /// Rejects edits to the append-only history, then reports unique-index violations without leaking SQL types:
    /// the open-fingerprint index becomes <see cref="DuplicateIncidentException"/> (a <see cref="ConflictException"/>),
    /// any other one <see cref="DuplicateKeyException"/> (still a <see cref="DbUpdateException"/>). Handlers turn both into a 409.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureHistoryIsAppendOnly();

        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql)
        {
            if (sql.Message.Contains(IncidentConfiguration.FingerprintIndexName, StringComparison.Ordinal))
            {
                throw new DuplicateIncidentException();
            }

            throw new DuplicateKeyException("A record with the same unique value already exists.", sql);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureHistoryIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasIncidentNumberSequence();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
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
}
