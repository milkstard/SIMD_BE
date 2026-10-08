using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<MonitoredApp> MonitoredApps => Set<MonitoredApp>();

    public DbSet<AppTeamMember> AppTeamMembers => Set<AppTeamMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
