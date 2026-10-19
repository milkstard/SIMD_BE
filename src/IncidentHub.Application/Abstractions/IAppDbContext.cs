using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace IncidentHub.Application.Abstractions;

/// <summary>Query/command access to the SQL store (ADR 0001: core EF Core only, no provider types).</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<Team> Teams { get; }

    DbSet<TeamMember> TeamMembers { get; }

    DbSet<MonitoredApp> Applications { get; }

    DbSet<Incident> Incidents { get; }

    /// <summary>Append-only audit rows; read them through <see cref="Incident.History"/> or query directly for paging.</summary>
    DbSet<IncidentHistory> IncidentHistory { get; }

    DbSet<Comment> Comments { get; }

    DbSet<Attachment> Attachments { get; }

    DbSet<SlaPolicySetting> SlaPolicies { get; }

    /// <summary>Lets a Worker use case drop the tracked state of an aggregate whose save failed.</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
