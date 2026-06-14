namespace Aethra.Modules.Notifications.Infrastructure;

/// <summary>
/// Retención de <c>NotificationDelivery</c>: se inserta una fila por envío y nunca se purgaban →
/// crecimiento ilimitado. El worker borra deliveries anteriores a <see cref="RetentionDays"/>.
/// Default 30d. Sección "Notifications" (env: Notifications__RetentionDays).
/// </summary>
public sealed class NotificationsRetentionOptions
{
    /// <summary>Días de deliveries a conservar. 0 o negativo = desactiva la purga. Default 30.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Cada cuántas horas barrer y purgar. Default 12.</summary>
    public double SweepIntervalHours { get; set; } = 12;
}
