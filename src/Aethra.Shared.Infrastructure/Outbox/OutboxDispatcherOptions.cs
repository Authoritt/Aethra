namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// Opciones de los dispatchers de outbox por módulo. El <c>BatchSize</c> acota cuántas filas
/// se procesan por tick para no monopolizar la conexión, y <c>PollIntervalMs</c> evita el
/// busy-loop cuando no hay trabajo pendiente.
/// </summary>
public sealed class OutboxDispatcherOptions
{
    public int BatchSize { get; set; } = 50;
    public int PollIntervalMs { get; set; } = 2000;

    /// <summary>
    /// Días que se conservan los mensajes de outbox YA procesados (<c>ProcessedAt != null</c>) antes
    /// de purgarlos. El dispatcher marca <c>ProcessedAt</c> pero nunca borra la fila → sin esto la
    /// tabla <c>outbox_messages</c> de cada módulo crece sin tope. 0 o negativo desactiva la purga.
    /// Solo afecta a procesados: un mensaje pendiente/fallido nunca se borra. Default 7.
    /// </summary>
    public int OutboxRetentionDays { get; set; } = 7;

    /// <summary>
    /// Cada cuántas horas corre la purga de mensajes procesados (por módulo). Default 12.
    /// </summary>
    public int OutboxSweepIntervalHours { get; set; } = 12;
}
