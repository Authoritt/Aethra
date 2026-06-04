using Aethra.Api.Bootstrap;
using Aethra.Shared.Contracts.Monitoring;
using Aethra.Shared.Contracts.Vms;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Aethra.Api.Hubs;

/// <summary>
/// Hub al que el frontend (Next.js) se conecta para recibir actualizaciones en tiempo real:
/// métricas de VMs, eventos de deploy, alertas de monitores, etc.
///
/// Suscripción por entidad: el cliente llama <c>JoinVm(vmId)</c> para recibir solo updates
/// de esa VM. Para vista global usa el grupo "all".
/// </summary>
[Authorize(AuthenticationSchemes = AuthSchemes.Cookie)]
public sealed class DashboardHub : Hub
{
    public Task JoinVm(string vmId)
        => Groups.AddToGroupAsync(Context.ConnectionId, VmGroup(vmId));

    public Task LeaveVm(string vmId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, VmGroup(vmId));

    public Task JoinMonitor(string monitorId)
        => Groups.AddToGroupAsync(Context.ConnectionId, MonitorGroup(monitorId));

    public Task LeaveMonitor(string monitorId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, MonitorGroup(monitorId));

    public override Task OnConnectedAsync()
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, "all");
    }

    public static string VmGroup(string vmId) => $"vm:{vmId}";
    public static string MonitorGroup(string monitorId) => $"monitor:{monitorId}";
}

/// <summary>
/// Suscriptor del bus de integración que reenvía eventos de métricas al frontend
/// vía <see cref="DashboardHub"/>. Vive en el host (no en un módulo) porque conoce el hub.
/// </summary>
internal sealed class DashboardForwarder(IHubContext<DashboardHub> hub, ILogger<DashboardForwarder> logger)
    : INotificationHandler<VmMetricsReportedEvent>, INotificationHandler<ContainersReportedEvent>,
      INotificationHandler<SatelliteConnectedEvent>, INotificationHandler<SatelliteDisconnectedEvent>,
      INotificationHandler<MonitorStatusChangedIntegrationEvent>
{
    public Task Handle(VmMetricsReportedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogDebug("Forward VmMetrics to dashboard for {VmId}", notification.VmId);
        var s = notification.Snapshot;
        // Mismo shape plano que el DTO REST (GetLatestMetricsQuery.VmMetricPoint): disco agregado
        // y red aplanada, para que la gráfica en vivo y la histórica consuman idéntica forma.
        long diskUsed = 0, diskTotal = 0;
        foreach (var d in s.Disks)
        {
            diskUsed += d.UsedBytes;
            diskTotal += d.TotalBytes;
        }
        var point = new
        {
            timestamp = s.Timestamp,
            cpuPercent = s.CpuPercent,
            memoryUsedBytes = s.MemoryUsedBytes,
            memoryTotalBytes = s.MemoryTotalBytes,
            diskUsedBytes = diskUsed,
            diskTotalBytes = diskTotal,
            netBytesReceived = s.Network.BytesReceived,
            netBytesSent = s.Network.BytesSent,
        };
        return hub.Clients.Group(DashboardHub.VmGroup(notification.VmId))
            .SendAsync("VmMetricsUpdated", notification.VmId, point, cancellationToken);
    }

    public Task Handle(ContainersReportedEvent notification, CancellationToken cancellationToken)
        => hub.Clients.Group(DashboardHub.VmGroup(notification.VmId))
            .SendAsync("VmContainersUpdated", notification.VmId, notification.Snapshot, cancellationToken);

    public Task Handle(SatelliteConnectedEvent notification, CancellationToken cancellationToken)
        => hub.Clients.Group("all")
            .SendAsync("VmStatusChanged", notification.VmId, "Connected", cancellationToken);

    public Task Handle(SatelliteDisconnectedEvent notification, CancellationToken cancellationToken)
        => hub.Clients.Group("all")
            .SendAsync("VmStatusChanged", notification.VmId, "Disconnected", cancellationToken);

    public Task Handle(MonitorStatusChangedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        logger.LogDebug("Forward MonitorStatusChanged for {MonitorId}: {From} → {To}",
            notification.MonitorId, notification.From, notification.To);
        // Mandamos a "all" para que la lista global se actualice y al grupo específico del monitor
        // para que la página de detalle reaccione sin polling.
        var payload = new
        {
            monitorId = notification.MonitorId,
            from = notification.From,
            to = notification.To,
            checkId = notification.CheckId,
            httpStatusCode = notification.HttpStatusCode,
            latencyMs = notification.LatencyMs,
            timestamp = notification.Timestamp,
        };
        var tasks = new[]
        {
            hub.Clients.Group("all").SendAsync("MonitorStatusChanged", payload, cancellationToken),
            hub.Clients.Group(DashboardHub.MonitorGroup(notification.MonitorId))
                .SendAsync("MonitorStatusChanged", payload, cancellationToken),
        };
        return Task.WhenAll(tasks);
    }
}

public static class DashboardHubMap
{
    public static IEndpointRouteBuilder MapDashboardHub(this IEndpointRouteBuilder app)
    {
        app.MapHub<DashboardHub>("/hubs/dashboard");
        return app;
    }
}
