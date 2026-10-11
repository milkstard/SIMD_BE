using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class IncidentHistoryConfiguration : IEntityTypeConfiguration<IncidentHistory>
{
    public void Configure(EntityTypeBuilder<IncidentHistory> builder)
    {
        builder.ToTable("IncidentHistory");
        builder.HasKey(h => h.Id);

        // The domain assigns the Guid. Without this EF would treat a new row reached through Incident.History as existing (Modified).
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.Field).HasMaxLength(IncidentHistory.FieldMaxLength).IsRequired();
        builder.Property(h => h.OldValue).HasColumnType("nvarchar(max)");
        builder.Property(h => h.NewValue).HasColumnType("nvarchar(max)");
        builder.Property(h => h.Note).HasMaxLength(IncidentHistory.NoteMaxLength);

        builder.HasIndex(h => new { h.IncidentId, h.CreatedAt }).HasDatabaseName("IX_IncidentHistory_Incident_CreatedAt");
        builder.HasOne<User>().WithMany().HasForeignKey(h => h.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
