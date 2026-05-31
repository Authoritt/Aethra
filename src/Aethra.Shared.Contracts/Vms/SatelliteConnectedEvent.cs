namespace Aethra.Shared.Contracts.Vms;

/// <summary>
/// Un satélite acaba de conectarse al hub central. Útil para refrescar métricas iniciales y notificar UI.
/// </summary>
public sealed record SatelliteConnectedEvent(
    string VmId,
    string Hostname,
    string KernelVersion,
    string CpuModel,
    int CpuCores,
    long TotalMemoryBytes
) : IntegrationEvent;

public sealed record SatelliteDisconnectedEvent(
    string VmId,
    string Reason
) : IntegrationEvent;
