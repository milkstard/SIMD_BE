using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.EntraObjectId).HasMaxLength(64).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.HasIndex(u => u.EntraObjectId).IsUnique();
    }
}
