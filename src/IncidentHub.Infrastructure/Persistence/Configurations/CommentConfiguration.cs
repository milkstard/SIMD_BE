using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments");
        builder.HasKey(c => c.Id);

        // The domain assigns the Guid; without this EF would treat a new comment as existing.
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Body).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(c => c.IsInternal).HasDefaultValue(false);

        builder.HasIndex(c => new { c.IncidentId, c.CreatedAt }).HasDatabaseName("IX_Comments_Incident_CreatedAt");
        builder.HasOne<Incident>().WithMany().HasForeignKey(c => c.IncidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);
    }
}
