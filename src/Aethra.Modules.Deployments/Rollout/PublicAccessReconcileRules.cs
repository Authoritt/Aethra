namespace Aethra.Modules.Deployments.Rollout;

public enum DirectIngressAction
{
    /// <summary>El host ya tiene ingress en el túnel Direct: no se toca.</summary>
    Skip = 0,

    /// <summary>El host no tiene ingress Direct: se bloquea y se manda a redesplegar.</summary>
    BlockedRedeploy = 1,
}

/// <summary>Qué hace el reconcile con el ingress del túnel de un host (OT-008 H3).</summary>
public enum TunnelIngressStep
{
    /// <summary>Central: el host ya tiene ingress, nada que hacer.</summary>
    AlreadyPresent = 0,

    /// <summary>Central: falta el ingress y el reconcile lo asegura (EnsureTunnelHostnameCommand).</summary>
    EnsureIngress = 1,

    /// <summary>Direct: el host ya tiene ingress, el backend lo gestiona el despliegue.</summary>
    DirectSkip = 2,

    /// <summary>Direct: falta el ingress; se bloquea y NUNCA se llama a Ensure.</summary>
    DirectBlockedRedeploy = 3,
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

    /// <summary>
    /// Paso del reconcile sobre el ingress del túnel. Con túnel Direct jamás devuelve
    /// <see cref="TunnelIngressStep.EnsureIngress"/>: reescribir el ingress con
    /// <c>Instance.ContainerName</c> dejaría el host apuntando a un contenedor inexistente (N1).
    /// </summary>
    public static TunnelIngressStep PlanTunnelIngress(bool isDirectTunnel, bool hostHasIngress)
        => isDirectTunnel
            ? (hostHasIngress ? TunnelIngressStep.DirectSkip : TunnelIngressStep.DirectBlockedRedeploy)
            : (hostHasIngress ? TunnelIngressStep.AlreadyPresent : TunnelIngressStep.EnsureIngress);

    public static DirectIngressDecision DecideDirectIngress(bool hostHasIngress, string hostname, string tunnelId)
        => hostHasIngress
            ? new DirectIngressDecision(
                DirectIngressAction.Skip,
                $"El túnel Direct {tunnelId} ya tiene ingress para {hostname}; su backend lo gestiona el despliegue.")
            : new DirectIngressDecision(
                DirectIngressAction.BlockedRedeploy,
                $"El túnel Direct {tunnelId} no tiene ingress para {hostname}: Direct se reconcilia redesplegando la instancia.");
}
