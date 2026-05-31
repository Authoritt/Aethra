using System.Security.Claims;
using Aethra.Modules.Vms.Authentication;
using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Contracts.Vms;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Time;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Vms.Presentation;

/// <summary>
/// Hub SignalR del satélite. El satélite envía:
/// - <c>Handshake</c>: una sola vez al conectarse.
/// - <c>ReportMetrics</c>: cada 5s.
/// - <c>ReportContainers</c>: cada 10s.
/// El central nunca llama métodos del satélite (por ahora — eso será F4 con BuildImage/RunContainer).
/// </summary>
[Authorize(AuthenticationSchemes = SatelliteAuthSchemes.TokenHeader)]
public sealed class SatelliteHub(
    VmsDbContext db,
    IClock clock,
    IIntegrationEventBus integrationBus,
    ILogger<SatelliteHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var vmId = ResolveVmId();
        if (vmId is null)
        {
            Context.Abort();
            return;
        }
        logger.LogInformation("Satélite conectado para VM {VmId} con connectionId={Conn}",
            vmId, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(vmId.Value));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var vmId = ResolveVmId();
        if (vmId is not null)
        {
            var vm = await db.Vms.FindAsync([vmId], Context.ConnectionAborted);
            if (vm is not null)
            {
                vm.RecordDisconnected(exception?.Message, clock.UtcNow);
                await db.SaveChangesAsync(Context.ConnectionAborted);
            }
            await integrationBus.PublishAsync(new SatelliteDisconnectedEvent(
                vmId.ToString()!, exception?.Message ?? "client_closed"), Context.ConnectionAborted);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Handshake(SatelliteHandshake info)
    {
        var vmId = ResolveVmId() ?? throw new HubException("VM no resoluble.");
        var vm = await db.Vms.FindAsync([vmId], Context.ConnectionAborted)
            ?? throw new HubException($"VM {vmId} no existe.");

        vm.RecordConnected(info.Hostname, info.KernelVersion, info.CpuModel, info.CpuCores,
            info.TotalMemoryBytes, info.AgentVersion, clock.UtcNow);
        await db.SaveChangesAsync(Context.ConnectionAborted);

        await integrationBus.PublishAsync(new SatelliteConnectedEvent(
            VmId: vmId.ToString()!,
            Hostname: info.Hostname,
            KernelVersion: info.KernelVersion,
            CpuModel: info.CpuModel,
            CpuCores: info.CpuCores,
            TotalMemoryBytes: info.TotalMemoryBytes), Context.ConnectionAborted);
    }

    /// <summary>
    /// El satélite envía un snapshot de métricas. Lo publicamos como integration event
    /// para que <c>Modules.Metrics</c> lo persista y <c>DashboardHub</c> lo reenvíe al frontend.
    /// </summary>
    public async Task ReportMetrics(VmMetricSnapshot snapshot)
    {
        var vmId = ResolveVmId() ?? throw new HubException("VM no resoluble.");
        await integrationBus.PublishAsync(
            new VmMetricsReportedEvent(vmId.ToString()!, snapshot),
            Context.ConnectionAborted);
    }

    public async Task ReportContainers(ContainerListSnapshot snapshot)
    {
        var vmId = ResolveVmId() ?? throw new HubException("VM no resoluble.");
        await integrationBus.PublishAsync(
            new ContainersReportedEvent(vmId.ToString()!, snapshot),
            Context.ConnectionAborted);
    }

    private VmId? ResolveVmId()
    {
        var claim = Context.User?.FindFirst(SatelliteAuthSchemes.VmIdClaim)?.Value;
        if (string.IsNullOrWhiteSpace(claim))
        {
            return null;
        }
        return Aethra.Shared.Kernel.Ids.AethraId.TryParse(claim, out var parsed)
            ? new VmId(parsed.Value)
            : null;
    }

    public static string GroupName(VmId id) => $"vm:{id}";
}

public static class SatelliteHubMap
{
    public static IEndpointRouteBuilder MapSatelliteHub(this IEndpointRouteBuilder app)
    {
        app.MapHub<SatelliteHub>("/hubs/satellite");
        return app;
    }
}
