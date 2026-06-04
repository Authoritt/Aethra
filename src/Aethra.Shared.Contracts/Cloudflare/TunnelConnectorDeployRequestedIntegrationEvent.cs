namespace Aethra.Shared.Contracts.Cloudflare;

/// <summary>
/// F13.12 — solicita desplegar el connector cloudflared (contenedor gestionado) del túnel en su VM.
/// Emitido por la tool MCP <c>aethra_deploy_tunnel_connector</c>; el host lo consume y corre el
/// connector en background (vía el RPC RunSpec del satélite). <paramref name="VmId"/> null = usa el
/// TargetVmId del túnel.
/// </summary>
public sealed record TunnelConnectorDeployRequestedIntegrationEvent(
    string? VmId,
    string Reason) : IntegrationEvent;
