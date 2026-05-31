namespace Aethra.Shared.Contracts.Vms;

/// <summary>
/// Evento cross-module: un satélite reportó métricas de su VM.
/// Consumido por <c>Modules.Metrics</c> para persistir y <c>DashboardHub</c> para push al frontend.
/// </summary>
public sealed record VmMetricsReportedEvent(string VmId, VmMetricSnapshot Snapshot) : IntegrationEvent;

public sealed record ContainersReportedEvent(string VmId, ContainerListSnapshot Snapshot) : IntegrationEvent;
