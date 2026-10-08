using System.Text.Json;
using System.Text.Json.Serialization;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class MonitoredAppConfiguration : IEntityTypeConfiguration<MonitoredApp>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<MonitoredApp> builder)
    {
        builder.ToTable("Applications");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(MonitoredApp.NameMaxLength).IsRequired();
        builder.Property(a => a.Code).HasMaxLength(MonitoredApp.CodeMaxLength).IsRequired();
        builder.Property(a => a.IsActive).HasDefaultValue(true);
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.Property(a => a.Environments)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => (IReadOnlyList<AppEnvironment>)JsonSerializer.Deserialize<List<AppEnvironment>>(v, JsonOptions)!,
                new ValueComparer<IReadOnlyList<AppEnvironment>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, e) => HashCode.Combine(hash, e)),
                    v => v.ToList()));

        builder.HasIndex(a => a.Code).IsUnique();
        builder.HasOne<Team>().WithMany().HasForeignKey(a => a.OwningTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.EscalationUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
