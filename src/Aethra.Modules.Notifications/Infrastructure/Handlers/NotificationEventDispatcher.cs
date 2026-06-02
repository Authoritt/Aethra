using System.Text.Json;
using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Infrastructure;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Notifications.Infrastructure.Handlers;

/// <summary>
/// Helper que cada handler de integration event invoca para fan-out a los canales activos:
/// resuelve los canales con <see cref="NotificationChannel.MatchesEvent"/> y encola una
/// <see cref="NotificationDelivery"/> Pending por canal. El dispatcher BackgroundService la
/// procesa fuera de banda.
///
/// Centralizado en un solo lugar para no duplicar el query+filter+enqueue en cada handler.
/// </summary>
internal sealed class NotificationEventDispatcher(
    NotificationsDbContext db,
    IClock clock,
    ILogger<NotificationEventDispatcher> logger)
{
    public async Task DispatchAsync(string eventType, object payload, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        var channels = await db.NotificationChannels
            .Where(c => c.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var matching = channels.Where(c => c.MatchesEvent(eventType)).ToList();
        if (matching.Count == 0)
        {
            logger.LogDebug("Notification event {EventType}: no hay canales matching", eventType);
            return;
        }

        var payloadJson = JsonSerializer.Serialize(payload);
        var now = clock.UtcNow;

        foreach (var channel in matching)
        {
            var delivery = NotificationDelivery.Queue(channel.Id, eventType, payloadJson, now);
            db.NotificationDeliveries.Add(delivery);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation(
            "Notification event {EventType}: encoladas {Count} deliveries", eventType, matching.Count);
    }
}
