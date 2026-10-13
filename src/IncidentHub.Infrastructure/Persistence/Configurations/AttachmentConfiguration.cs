using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");
        builder.HasKey(a => a.Id);

        // The handler assigns the Guid before the upload; without this EF would treat a new attachment as existing.
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.FileName).HasMaxLength(Attachment.FileNameMaxLength).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(Attachment.ContentTypeMaxLength).IsRequired();
        builder.Property(a => a.BlobPath).HasMaxLength(Attachment.BlobPathMaxLength).IsRequired();
        builder.Property(a => a.ScanStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

        // A concurrency token: linking is UPDATE ... WHERE IncidentId IS NULL, so of two parallel links exactly one updates the row.
        builder.Property(a => a.IncidentId).IsConcurrencyToken();

        builder.HasIndex(a => a.IncidentId).HasDatabaseName("IX_Attachments_IncidentId");

        // Orphan sweep and "my unlinked uploads": only the rows that are not linked yet.
        builder.HasIndex(a => new { a.UploadedById, a.CreatedAt })
            .HasDatabaseName("IX_Attachments_Unlinked")
            .HasFilter("[IncidentId] IS NULL");

        builder.Property(a => a.ScanAttempts).HasDefaultValue(0);

        // The scan job claims the Pending rows with the fewest failed attempts first, then the oldest.
        builder.HasIndex(a => new { a.ScanAttempts, a.CreatedAt })
            .HasDatabaseName("IX_Attachments_Pending")
            .HasFilter("[ScanStatus] = 'Pending'");

        builder.HasOne<Incident>().WithMany().HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Comment>().WithMany().HasForeignKey(a => a.CommentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UploadedById).OnDelete(DeleteBehavior.Restrict);
    }
}
