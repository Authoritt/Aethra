using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;
using Aethra.Shared.Contracts.Containers;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aethra.Api.Bootstrap;

public sealed record ConnectorDeployResult(bool Success, string? Error, string? VmId, string ContainerName);

/// <summary>
/// F13.11 — despliega el connector de cloudflared como CONTENEDOR gestionado por Aethra (vía el RPC
/// RunSpec que ya existe), corriendo con el connector token (config remota). Así el "flip" a túnel
/// remoto se hace 100% desde la UI, sin tocar el systemd del host ni abrir una superficie de shell
/// arbitraria en el satélite — es solo un contenedor más (network host para alcanzar localhost:5080/443).
/// Múltiples connectors del mismo túnel con --token son réplicas HA válidas (no conflictan).
/// </summary>
public sealed class CloudflareConnectorDeployer(
    IMediator mediator,
    ISatelliteRpcClient satellite,
    ISatelliteConnectionRegistry registry,
    IConfiguration config,
    ILogger<CloudflareConnectorDeployer> log)
{
    private const string ContainerName = "aethra-cf-connector";
    private const string Image = "cloudflare/cloudflared:latest";
    private readonly string _appNetwork =
        config["Deployments:AppNetwork"] is { Length: > 0 } n ? n : "aethra-net";

    public async Task<ConnectorDeployResult> DeployAsync(string? vmId, CancellationToken ct)
        => await DeployAsync(vmId, tunnelId: null, mode: null, ct).ConfigureAwait(false);

    public async Task<ConnectorDeployResult> DeployAsync(string? vmId, string? tunnelId, string? mode, CancellationToken ct)
    {
        var normalizedVmId = Normalize(vmId);
        var normalizedTunnelId = Normalize(tunnelId);
        var normalizedMode = NormalizeMode(mode);
        if (normalizedMode == "invalid")
        {
            return new ConnectorDeployResult(false, $"Modo de túnel no soportado: '{mode}'.", normalizedVmId, ContainerName);
        }

        var tunnelResult = await ResolveTunnelAsync(normalizedVmId, normalizedTunnelId, normalizedMode, ct).ConfigureAwait(false);
        if (!tunnelResult.Success)
        {
            return new ConnectorDeployResult(false, tunnelResult.Error, normalizedVmId, ContainerName);
        }

        var tunnel = tunnelResult.Tunnel;
        var isDirect = string.Equals(tunnel.Mode, "direct", StringComparison.OrdinalIgnoreCase);
        // El connector Direct debe correr en la VM dueña del túnel; el camino central conserva el override histórico.
        var targetVm = isDirect ? tunnel.TargetVmId : normalizedVmId ?? tunnel.TargetVmId;
        if (string.IsNullOrWhiteSpace(targetVm))
        {
            return new ConnectorDeployResult(false,
                "Falta el TargetVmId del túnel (la VM donde corre el connector). Regístralo al conectar el túnel o pásalo explícito.",
                null, ContainerName);
        }
        if (!registry.IsConnected(targetVm))
        {
            return new ConnectorDeployResult(false, $"La VM '{targetVm}' no tiene satélite conectado.", targetVm, ContainerName);
        }

        var tokenResult = await mediator.Send(new GetConnectorTokenQuery(tunnel.TunnelId), ct).ConfigureAwait(false);
        if (tokenResult.IsFailure)
        {
            return new ConnectorDeployResult(false, tokenResult.Error.Message, targetVm, ContainerName);
        }

        var spec = new RunSpec(
            ContainerName: ContainerName,
            ImageRef: Image,
            // cloudflared lee TUNNEL_TOKEN del entorno; el token NO viaja en la línea de comandos.
            Env: new Dictionary<string, string> { ["TUNNEL_TOKEN"] = tokenResult.Value },
            Ports: [],
            Volumes: [],
            Command: ["tunnel", "--no-autoupdate", "run"],
            Healthcheck: null,
            // CentralProxy conserva host para alcanzar localhost:5080/443; Direct entra a los contenedores por DNS Docker.
            NetworkName: isDirect ? _appNetwork : "host",
            RestartPolicy: "unless-stopped");

        try
        {
            await satellite.SendRemoveAsync(targetVm, ContainerName, force: true, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogDebug("cf-connector: remove previo ignorado: {Msg}", ex.Message);
        }

        var run = await satellite.SendRunAsync(targetVm, spec, pullFrom: null, ct).ConfigureAwait(false);
        if (!run.Success)
        {
            return new ConnectorDeployResult(false, run.ErrorMessage ?? "no arrancó", targetVm, ContainerName);
        }
        log.LogInformation("cf-connector desplegado en VM {Vm} (contenedor {Name}, red {Network})",
            targetVm, ContainerName, spec.NetworkName);
        return new ConnectorDeployResult(true, null, targetVm, ContainerName);
    }

    private async Task<(bool Success, CloudflareTunnelDto Tunnel, string? Error)> ResolveTunnelAsync(
        string? vmId,
        string? tunnelId,
        string? mode,
        CancellationToken ct)
    {
        if (mode == "direct" && string.IsNullOrWhiteSpace(tunnelId))
        {
            if (string.IsNullOrWhiteSpace(vmId))
            {
                return (false, null!, "mode=direct requiere tunnelId o vmId para resolver el túnel.");
            }

            var byVm = await mediator.Send(new GetTunnelForVmQuery(vmId), ct).ConfigureAwait(false);
            if (byVm.IsFailure)
            {
                return (false, null!, byVm.Error.Message);
            }

            return byVm.Value is null
                ? (false, null!, $"No hay túnel Direct registrado para la VM '{vmId}'.")
                : (true, byVm.Value, null);
        }
        if (mode is null && string.IsNullOrWhiteSpace(tunnelId) && !string.IsNullOrWhiteSpace(vmId))
        {
            var byVm = await mediator.Send(new GetTunnelForVmQuery(vmId), ct).ConfigureAwait(false);
            if (byVm.IsFailure)
            {
                return (false, null!, byVm.Error.Message);
            }
            if (byVm.Value is not null)
            {
                return (true, byVm.Value, null);
            }
        }

        var query = await mediator.Send(new GetTunnelQuery(tunnelId), ct).ConfigureAwait(false);
        if (query.IsFailure)
        {
            return (false, null!, query.Error.Message);
        }
        if (query.Value is null)
        {
            return (false, null!, "No hay túnel gestionado registrado.");
        }
        if (mode == "direct" && !string.Equals(query.Value.Mode, "direct", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null!, $"El túnel '{query.Value.TunnelId}' no es Direct.");
        }
        if (mode == "central_proxy" && !string.Equals(query.Value.Mode, "central_proxy", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null!, $"El túnel '{query.Value.TunnelId}' no es CentralProxy.");
        }

        return (true, query.Value, null);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        return mode.Trim().ToLowerInvariant().Replace("-", "_") switch
        {
            "centralproxy" or "central_proxy" => "central_proxy",
            "direct" => "direct",
            _ => "invalid",
        };
    }
}
