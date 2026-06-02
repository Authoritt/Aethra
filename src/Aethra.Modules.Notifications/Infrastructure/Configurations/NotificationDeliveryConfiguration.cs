using Aethra.Modules.Notifications.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Notifications.Infrastructure.Configurations;

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<NotificationDeliveryId, string>(
                id => id.ToString(),
                s => ParseId(s)))
            .HasMaxLength(64);

        builder.Property(d => d.ChannelId)
            .HasColumnName("channel_id")
            .HasConversion(new ValueConverter<NotificationChannelId, string>(
                id => id.ToString(),
                s => ParseChannelId(s)))
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(d => d.ChannelId)
            .HasDatabaseName("ix_notification_deliveries_channel");

        builder.Property(d => d.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        builder.Property(d => d.Payload).HasColumnName("payload").HasColumnType("text").IsRequired();

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(d => d.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(d => d.Error).HasColumnName("error").HasMaxLength(2000);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.SentAt).HasColumnName("sent_at");
        builder.Property(d => d.NextAttemptAt).HasColumnName("next_attempt_at");

        builder.HasIndex(d => new { d.Status, d.NextAttemptAt })
            .HasDatabaseName("ix_notification_deliveries_pending");

        builder.Ignore(d => d.DomainEvents);
    }

    private static NotificationDeliveryId ParseId(string s)
        => AethraId.TryParse(s, out var parsed) ? new NotificationDeliveryId(parsed.Value) : default;

    private static NotificationChannelId ParseChannelId(string s)
        => AethraId.TryParse(s, out var parsed) ? new NotificationChannelId(parsed.Value) : default;
}
