using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Abstractions;

/// <summary>Query/command access to the SQL store (ADR 0001: core EF Core only, no provider types).</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<Team> Teams { get; }

    DbSet<TeamMember> TeamMembers { get; }

    DbSet<MonitoredApp> Applications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
