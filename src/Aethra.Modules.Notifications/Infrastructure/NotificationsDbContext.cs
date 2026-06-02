using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Infrastructure.Configurations;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notifications.Infrastructure;

/// <summary>
/// DbContext del modulo Notifications. Schema PostgreSQL: <c>notifications</c>. Comparte
/// <c>outbox_messages</c> en el mismo schema heredado de la base.
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "notifications";

    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new NotificationChannelConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationDeliveryConfiguration());
    }
}
