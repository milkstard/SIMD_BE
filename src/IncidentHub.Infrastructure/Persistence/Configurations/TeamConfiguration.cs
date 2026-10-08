using IncidentHub.Domain.Teams;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(Team.NameMaxLength).IsRequired();
        builder.Property(t => t.Email).HasMaxLength(Team.EmailMaxLength).IsRequired();
        builder.Property(t => t.TeamsChannelUrl).HasMaxLength(Team.ChannelUrlMaxLength);
        builder.Property(t => t.RowVersion).IsRowVersion();
    }
}
