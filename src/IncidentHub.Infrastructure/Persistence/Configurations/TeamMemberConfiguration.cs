using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("TeamMembers");
        builder.HasKey(m => new { m.TeamId, m.UserId });
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.HasIndex(m => m.UserId);
        builder.HasOne<Team>().WithMany().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
