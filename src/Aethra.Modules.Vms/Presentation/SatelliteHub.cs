using System.Security.Claims;
using Aethra.Modules.Vms.Authentication;
using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Contracts.Containers;
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
using Microsoft.Extensions.DependencyInjection;
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

    // -------------------------------------------------------------------------
    // Respuestas RPC del satélite (canal central → satélite → central). El satélite
    // invoca estos métodos como callback al request original; cada uno resuelve el
    // TaskCompletionSource pendiente registrado por SignalRSatelliteRpcClient.
    // -------------------------------------------------------------------------

    public Task BuildImageResponse(BuildImageResponse response)
    {
        Callbacks().CompleteRequest(response.CorrelationId, response);
        return Task.CompletedTask;
    }

    public Task RunContainerResponse(RunContainerResponse response)
    {
        Callbacks().CompleteRequest(response.CorrelationId, response);
        return Task.CompletedTask;
    }

    public Task StopContainerAck(string correlationId)
    {
        Callbacks().CompleteRequest(correlationId, new object());
        return Task.CompletedTask;
    }

    public Task RemoveContainerAck(string correlationId)
    {
        Callbacks().CompleteRequest(correlationId, new object());
        return Task.CompletedTask;
    }

    public Task ListContainersResponse(ListContainersResponse response)
    {
        Callbacks().CompleteRequest(response.CorrelationId, response);
        return Task.CompletedTask;
    }

    public Task RpcFailed(string correlationId, string errorMessage)
    {
        Callbacks().FailRequest(correlationId, new InvalidOperationException(errorMessage));
        return Task.CompletedTask;
    }

    public Task LogChunkPush(LogChunk chunk)
    {
        Callbacks().PushLogChunk(chunk);
        return Task.CompletedTask;
    }

    public Task LogStreamCompleted(string correlationId, string? errorMessage)
    {
        Callbacks().CompleteStream(
            correlationId,
            errorMessage is null ? null : new InvalidOperationException(errorMessage));
        return Task.CompletedTask;
    }

    /// <summary>Resuelve <see cref="ISatelliteRpcCallbacks"/> del scope del hub. Es opcional —
    /// si el host no registró una implementación (modo dry-run F9.6) emitimos un warning y
    /// descartamos el callback.</summary>
    private ISatelliteRpcCallbacks Callbacks()
    {
        var callbacks = Context.GetHttpContext()?.RequestServices.GetService<ISatelliteRpcCallbacks>();
        if (callbacks is null)
        {
            logger.LogWarning(
                "ISatelliteRpcCallbacks no registrado; respuesta del satélite ignorada (modo dry-run).");
            return NoopCallbacks.Instance;
        }
        return callbacks;
    }

    private sealed class NoopCallbacks : ISatelliteRpcCallbacks
    {
        public static readonly NoopCallbacks Instance = new();
        public void CompleteRequest(string correlationId, object response) { }
        public void FailRequest(string correlationId, Exception error) { }
        public void PushLogChunk(LogChunk chunk) { }
        public void CompleteStream(string correlationId, Exception? error = null) { }
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
