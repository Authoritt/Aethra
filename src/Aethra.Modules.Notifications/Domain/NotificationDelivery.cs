using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Notifications.Domain;

public enum NotificationDeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}

/// <summary>
/// Registro de una entrega individual al canal: payload, status y reintentos. El
/// dispatcher BackgroundService consume Pending y aplica backoff con jitter (Polly,
/// max 5 attempts) antes de marcarlo Failed.
/// </summary>
public sealed class NotificationDelivery : AggregateRoot<NotificationDeliveryId>
{
    public NotificationChannelId ChannelId { get; private set; }
    public string EventType { get; private set; }
    public string Payload { get; private set; }
    public NotificationDeliveryStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }

    private NotificationDelivery(
        NotificationDeliveryId id,
        NotificationChannelId channelId,
        string eventType,
        string payload,
        DateTimeOffset now) : base(id)
    {
        ChannelId = channelId;
        EventType = eventType;
        Payload = payload;
        Status = NotificationDeliveryStatus.Pending;
        CreatedAt = now;
        NextAttemptAt = now;
    }

    public static NotificationDelivery Queue(
        NotificationChannelId channelId,
        string eventType,
        string payload,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("EventType requerido.", nameof(eventType));
        }
        return new NotificationDelivery(
            NotificationDeliveryId.New(),
            channelId,
            eventType.Trim(),
            payload ?? string.Empty,
            now);
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = NotificationDeliveryStatus.Sent;
        SentAt = now;
        Attempts++;
        Error = null;
        NextAttemptAt = null;
    }

    public void MarkAttemptFailed(string error, DateTimeOffset? nextAttemptAt, DateTimeOffset now)
    {
        Attempts++;
        Error = Truncate(error, 2000);
        NextAttemptAt = nextAttemptAt;
        // Status sigue Pending mientras quedan reintentos.
    }

    public void MarkPermanentlyFailed(string error, DateTimeOffset now)
    {
        Status = NotificationDeliveryStatus.Failed;
        Error = Truncate(error, 2000);
        Attempts++;
        NextAttemptAt = null;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max]);

    // EF Core
    private NotificationDelivery() : base()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }
}
