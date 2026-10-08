using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class AppTeamMemberConfiguration : IEntityTypeConfiguration<AppTeamMember>
{
    public void Configure(EntityTypeBuilder<AppTeamMember> builder)
    {
        builder.ToTable("AppTeamMembers");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TeamRole).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(m => new { m.ApplicationId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);
        builder.HasOne<MonitoredApp>().WithMany().HasForeignKey(m => m.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
