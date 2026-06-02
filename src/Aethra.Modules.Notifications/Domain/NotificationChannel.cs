using Aethra.Modules.Notifications.Domain.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Notifications.Domain;

/// <summary>
/// Canal de notificacion (Slack/Discord/Telegram/Email/Webhook). El <see cref="ConfigCipher"/>
/// es un blob cifrado con DataProtection purpose <c>aethra-notification-config</c> cuyo shape
/// depende de <see cref="Type"/> (ver <see cref="NotificationChannelType"/>).
///
/// <see cref="EventFilters"/> es la lista de event types a los que esta suscripto. Empty = all
/// (escucha todos los eventos del catalogo <see cref="NotificationEventTypes"/>).
/// </summary>
public sealed class NotificationChannel : AggregateRoot<NotificationChannelId>
{
    public string Name { get; private set; }
    public NotificationChannelType Type { get; private set; }
    public byte[] ConfigCipher { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyList<string> EventFilters { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastDeliveredAt { get; private set; }

    private NotificationChannel(
        NotificationChannelId id,
        string name,
        NotificationChannelType type,
        byte[] configCipher,
        IReadOnlyList<string> eventFilters,
        DateTimeOffset now) : base(id)
    {
        Name = name;
        Type = type;
        ConfigCipher = configCipher;
        EventFilters = eventFilters;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static NotificationChannel Create(
        string name,
        NotificationChannelType type,
        byte[] configCipher,
        IEnumerable<string>? eventFilters,
        DateTimeOffset now)
    {
        ValidateName(name);
        if (configCipher is null || configCipher.Length == 0)
        {
            throw new ArgumentException("ConfigCipher requerido (config cifrada).", nameof(configCipher));
        }
        var filters = NormalizeFilters(eventFilters);

        var channel = new NotificationChannel(
            NotificationChannelId.New(),
            name.Trim(),
            type,
            configCipher,
            filters,
            now);
        channel.Raise(new NotificationChannelCreatedEvent(channel.Id, channel.Name, type));
        return channel;
    }

    public void UpdateConfig(byte[] newCipher, DateTimeOffset now)
    {
        if (newCipher is null || newCipher.Length == 0)
        {
            throw new ArgumentException("ConfigCipher requerido.", nameof(newCipher));
        }
        ConfigCipher = newCipher;
        UpdatedAt = now;
    }

    public void UpdateEventFilters(IEnumerable<string>? eventFilters, DateTimeOffset now)
    {
        EventFilters = NormalizeFilters(eventFilters);
        UpdatedAt = now;
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        if (IsActive == active) { return; }
        IsActive = active;
        UpdatedAt = now;
    }

    public void MarkDelivered(DateTimeOffset now)
    {
        LastDeliveredAt = now;
        UpdatedAt = now;
    }

    public void MarkDeleted()
    {
        Raise(new NotificationChannelDeletedEvent(Id, Name));
    }

    /// <summary>
    /// True si este canal debe recibir notificacion para el <paramref name="eventType"/> dado.
    /// Empty filters = match all. Filtros declarados = match exacto solamente.
    /// </summary>
    public bool MatchesEvent(string eventType)
    {
        if (!IsActive) { return false; }
        if (EventFilters.Count == 0) { return true; }
        return EventFilters.Contains(eventType, StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> NormalizeFilters(IEnumerable<string>? filters)
    {
        if (filters is null) { return Array.Empty<string>(); }
        var set = new List<string>();
        foreach (var f in filters)
        {
            if (string.IsNullOrWhiteSpace(f)) { continue; }
            var trimmed = f.Trim();
            if (!NotificationEventTypes.All.Contains(trimmed))
            {
                throw new ArgumentException($"Event type invalido: '{trimmed}'.", nameof(filters));
            }
            if (!set.Contains(trimmed, StringComparer.Ordinal))
            {
                set.Add(trimmed);
            }
        }
        return set;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name requerido.", nameof(name));
        }
        if (name.Trim().Length > 100)
        {
            throw new ArgumentException("Name no puede exceder 100 caracteres.", nameof(name));
        }
    }

    // EF Core
    private NotificationChannel() : base()
    {
        Name = string.Empty;
        ConfigCipher = [];
        EventFilters = Array.Empty<string>();
    }
}
