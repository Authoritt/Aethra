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
}
