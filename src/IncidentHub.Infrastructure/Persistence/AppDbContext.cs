using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
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

    /// <summary>
    /// Reports unique-index violations as <see cref="DuplicateKeyException"/> (still a <see cref="DbUpdateException"/>)
    /// so handlers can turn them into a 409 without referencing the SQL Server provider.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            throw new DuplicateKeyException("A record with the same unique value already exists.", ex.InnerException);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
