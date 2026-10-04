namespace Aethra.Modules.Deployments.Rollout;

public enum DirectIngressAction
{
    /// <summary>El host ya tiene ingress en el túnel Direct: no se toca.</summary>
    Skip = 0,

    /// <summary>El host no tiene ingress Direct: se bloquea y se manda a redesplegar.</summary>
    BlockedRedeploy = 1,
}

public sealed record DirectIngressDecision(DirectIngressAction Action, string Message);

public sealed record PublicAccessInfraChoice(bool IsDirect, string? TunnelCname);

/// <summary>
/// OT-008 T7b (N1/N6) — reglas puras del reconcile de acceso público para hosts Direct.
/// El ingress de un host Direct lo escribe el runner de despliegue con el backend
/// <c>http://{slug}-{svc}:{port}</c>; el reconcile NO lo reescribe con <c>Instance.ContainerName</c>
/// (valor distinto) porque dejaría el host apuntando a un contenedor que no existe.
/// </summary>
public static class PublicAccessReconcileRules
{
    public static string TunnelCnameFor(string tunnelId) => $"{tunnelId}.cfargotunnel.com";

    /// <summary>
    /// Elige la infra de la instancia: con túnel Direct para su VM, el CNAME es el de ese túnel;
    /// sin él (caso de las 7 instancias de producción), se conserva la infra central tal cual.
    /// </summary>
    public static PublicAccessInfraChoice ChooseInfra(string? directTunnelId, string? centralTunnelCname)
        => string.IsNullOrWhiteSpace(directTunnelId)
            ? new PublicAccessInfraChoice(false, centralTunnelCname)
            : new PublicAccessInfraChoice(true, TunnelCnameFor(directTunnelId.Trim()));

    public static DirectIngressDecision DecideDirectIngress(bool hostHasIngress, string hostname, string tunnelId)
        => hostHasIngress
            ? new DirectIngressDecision(
                DirectIngressAction.Skip,
                $"El túnel Direct {tunnelId} ya tiene ingress para {hostname}; su backend lo gestiona el despliegue.")
            : new DirectIngressDecision(
                DirectIngressAction.BlockedRedeploy,
                $"El túnel Direct {tunnelId} no tiene ingress para {hostname}: Direct se reconcilia redesplegando la instancia.");
}
