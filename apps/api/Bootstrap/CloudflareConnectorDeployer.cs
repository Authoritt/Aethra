using Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;
using Aethra.Shared.Contracts.Containers;
using MediatR;
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
    ILogger<CloudflareConnectorDeployer> log)
{
    private const string ContainerName = "aethra-cf-connector";
    private const string Image = "cloudflare/cloudflared:latest";

    public async Task<ConnectorDeployResult> DeployAsync(string? vmId, CancellationToken ct)
    {
        var targetVm = vmId;
        if (string.IsNullOrWhiteSpace(targetVm))
        {
            targetVm = registry.ConnectedVmIds.FirstOrDefault();
        }
        if (string.IsNullOrWhiteSpace(targetVm))
        {
            return new ConnectorDeployResult(false, "No hay ninguna VM con satélite conectado.", null, ContainerName);
        }

        var tokenResult = await mediator.Send(new GetConnectorTokenQuery(), ct).ConfigureAwait(false);
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
            // network host: el connector debe alcanzar los servicios en localhost del host (5080/443).
            NetworkName: "host",
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
        log.LogInformation("cf-connector desplegado en VM {Vm} (contenedor {Name})", targetVm, ContainerName);
        return new ConnectorDeployResult(true, null, targetVm, ContainerName);
    }
}
