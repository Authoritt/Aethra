namespace Aethra.Shared.Contracts.Vms;

/// <summary>
/// Notifica al frontend (vía DashboardHub) el progreso de un install remoto del satélite.
/// Vive en <c>Shared.Contracts</c> porque la implementación necesita <see cref="DashboardHub"/>
/// (que vive en apps/api) y los consumidores (módulo Vms) no pueden referenciar al host.
/// </summary>
public interface IInstallProgressNotifier
{
    /// <summary>
    /// Emite una línea de log al grupo de la VM en el DashboardHub.
    /// El frontend escucha el evento <c>VmInstallLog</c> con payload <c>{ vmId, line, level }</c>.
    /// </summary>
    /// <param name="level">"info" | "warn" | "error" | "debug".</param>
    Task PublishLogAsync(string vmId, string line, string level = "info",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emite el cambio de estado de instalación (Installing → Installed/Failed) al grupo de la VM.
    /// Evento SignalR: <c>VmInstallStatusChanged</c> con payload <c>{ vmId, status, errorCode? }</c>.
    /// </summary>
    Task PublishStatusAsync(string vmId, string status, string? errorCode = null,
        CancellationToken cancellationToken = default);
}
