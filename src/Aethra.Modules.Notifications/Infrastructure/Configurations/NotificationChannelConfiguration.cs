using Aethra.Modules.Notifications.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Notifications.Infrastructure.Configurations;

internal sealed class NotificationChannelConfiguration : IEntityTypeConfiguration<NotificationChannel>
{
    public void Configure(EntityTypeBuilder<NotificationChannel> builder)
    {
        builder.ToTable("notification_channels");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<NotificationChannelId, string>(
                id => id.ToString(),
                s => ParseId(s)))
            .HasMaxLength(64);

        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Name)
            .IsUnique()
            .HasDatabaseName("ux_notification_channels_name");

        builder.Property(c => c.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(c => c.ConfigCipher)
            .HasColumnName("config_cipher")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(c => c.IsActive).HasColumnName("is_active").IsRequired();

        // text[] de eventos. EF mapea directo a array PG cuando el tipo es IReadOnlyList<string>
        // y el backing field es string[]/List<string> — usamos comparer custom para change tracking.
        var stringArrayComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<string>>(
            (a, b) => a == null && b == null
                || (a != null && b != null && a.SequenceEqual(b, StringComparer.Ordinal)),
            v => v == null ? 0 : v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode(StringComparison.Ordinal))),
            v => v == null ? Array.Empty<string>() : v.ToArray());

        builder.Property(c => c.EventFilters)
            .HasColumnName("event_filters")
            .HasColumnType("text[]")
            .HasConversion(
                v => v.ToArray(),
                v => v)
            .Metadata.SetValueComparer(stringArrayComparer);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(c => c.LastDeliveredAt).HasColumnName("last_delivered_at");

        builder.Ignore(c => c.DomainEvents);
    }

    private static NotificationChannelId ParseId(string s)
        => AethraId.TryParse(s, out var parsed) ? new NotificationChannelId(parsed.Value) : default;
}
