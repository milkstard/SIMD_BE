using IncidentHub.Domain.Incidents.Sla;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class SlaPolicySettingConfiguration : IEntityTypeConfiguration<SlaPolicySetting>
{
    public void Configure(EntityTypeBuilder<SlaPolicySetting> builder)
    {
        builder.ToTable("SlaPolicies");
        builder.HasKey(p => p.Severity);
        builder.Property(p => p.Severity).HasConversion<string>().HasMaxLength(20).ValueGeneratedNever();
        builder.Property(p => p.AckMinutes).IsRequired();
        builder.Property(p => p.ResolveMinutes).IsRequired();
        builder.Property(p => p.EscalateAtPercent).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();
    }
}
