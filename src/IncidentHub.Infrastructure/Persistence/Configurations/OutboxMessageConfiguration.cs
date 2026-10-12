using IncidentHub.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentHub.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public const string UnprocessedIndexName = "IX_OutboxMessages_Unprocessed";

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Type).HasMaxLength(OutboxMessage.TypeMaxLength).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(m => m.Attempts).HasDefaultValue(0);
        builder.Property(m => m.Error).HasColumnType("nvarchar(max)");

        // The dispatcher polls undispatched rows in order (UPDLOCK, READPAST, BE-10).
        builder.HasIndex(m => m.ProcessedAt)
            .IncludeProperties(m => m.OccurredAt)
            .HasFilter("[ProcessedAt] IS NULL")
            .HasDatabaseName(UnprocessedIndexName);
    }
}
