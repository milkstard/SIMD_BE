using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public const string FingerprintIndexName = "IX_Incidents_Fingerprint_Open";

    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("Incidents");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Number)
            .HasDefaultValueSql(Sequences.NextIncidentNumberSql)
            .ValueGeneratedOnAdd();
        builder.HasIndex(i => i.Number).IsUnique().HasDatabaseName("IX_Incidents_Number");

        builder.Property(i => i.Environment).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(i => i.Title).HasMaxLength(300).IsRequired();
        builder.Property(i => i.Description).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(i => i.StepsToReproduce).HasColumnType("nvarchar(max)");
        builder.Property(i => i.Severity).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Fingerprint).HasMaxLength(300);
        builder.Property(i => i.ExternalId).HasMaxLength(200);
        builder.Property(i => i.IsSlaBreached).HasDefaultValue(false);
        builder.Property(i => i.CloseReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.RowVersion).IsRowVersion();

        builder.HasIndex(i => new { i.Status, i.Severity })
            .IncludeProperties(i => new { i.ApplicationId, i.AssigneeId })
            .HasDatabaseName("IX_Incidents_Status_Severity");
        builder.HasIndex(i => new { i.ApplicationId, i.CreatedAt })
            .HasDatabaseName("IX_Incidents_Application_CreatedAt");
        builder.HasIndex(i => i.Fingerprint)
            .IsUnique()
            .HasFilter("[Status] <> 'Closed' AND [Fingerprint] IS NOT NULL")
            .HasDatabaseName(FingerprintIndexName);
        builder.HasIndex(i => i.ResolveDueAt)
            .IncludeProperties(i => new { i.SlaWarnAt, i.SlaWarnedAt })
            .HasFilter("[IsSlaBreached] = 0 AND [Status] <> 'Resolved' AND [Status] <> 'Closed'")
            .HasDatabaseName("IX_Incidents_SlaCandidates");

        builder.HasOne<MonitoredApp>().WithMany().HasForeignKey(i => i.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.ReporterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(i => i.AssignedTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Incident>().WithMany().HasForeignKey(i => i.DuplicateOfId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.History)
            .WithOne()
            .HasForeignKey(h => h.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(i => i.DomainEvents);
    }
}
