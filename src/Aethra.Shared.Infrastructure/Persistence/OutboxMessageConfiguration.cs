using Aethra.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Shared.Infrastructure.Persistence;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Type).HasMaxLength(512).IsRequired();
        builder.Property(o => o.Payload).IsRequired();
        builder.Property(o => o.OccurredAt).IsRequired();
        builder.Property(o => o.ProcessedAt);
        builder.Property(o => o.Attempts).HasDefaultValue(0).IsRequired();
        builder.Property(o => o.Error).HasMaxLength(2000);
        builder.Property(o => o.NextAttemptAt);

        // Cubre el patrón de query del dispatcher (mensajes pendientes ordenados por tiempo).
        builder.HasIndex(o => new { o.ProcessedAt, o.NextAttemptAt, o.OccurredAt })
            .HasDatabaseName("ix_outbox_pending");
    }
}
