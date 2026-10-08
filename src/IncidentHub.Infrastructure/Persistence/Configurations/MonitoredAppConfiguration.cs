using IncidentHub.Domain.Apps;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class MonitoredAppConfiguration : IEntityTypeConfiguration<MonitoredApp>
{
    public void Configure(EntityTypeBuilder<MonitoredApp> builder)
    {
        builder.ToTable("MonitoredApps");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
    }
}
