namespace Aethra.Modules.Deployments.Rollout;

public enum EdgePlacementKind
{
    Central = 0,
    Direct = 1,
    Ambiguous = 2,
    Failed = 3,
}

public sealed record EdgeTunnelCandidate(string TunnelId, string TargetVmId);

public sealed record EdgePlacementDecision(
    EdgePlacementKind Kind,
    EdgeTunnelCandidate? Tunnel,
    IReadOnlyList<string> Warnings,
    string? Error = null);

public sealed record DirectRouteCandidate(
    string ServiceName,
    bool RequiresPublicRoute,
    string? ResolvedHost,
    IReadOnlyList<string> PathPrefixes);

public sealed record DirectRouteValidationResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>
/// OT-008 F1 — reglas puras que T5 conectará antes de <c>Deployment.Queue</c> en
/// <c>NativeDeployRunner.DeployAsync</c> (spec D6/F3, línea histórica 110).
/// </summary>
public static class EdgePlacementRules
{
    public const string MissingDirectTunnelWarning =
        "instancia en VM sin túnel Direct: el proxy central no alcanza sus contenedores";

    public static EdgePlacementDecision Resolve(
        string? instanceVmId,
        IEnumerable<EdgeTunnelCandidate> directTunnels,
        string? centralProxyVmId = null)
    {
        ArgumentNullException.ThrowIfNull(directTunnels);

        if (string.IsNullOrWhiteSpace(instanceVmId))
        {
            return new EdgePlacementDecision(EdgePlacementKind.Central, null, [MissingDirectTunnelWarning]);
        }

        var vmId = instanceVmId.Trim();
        var matches = directTunnels
            .Where(t => string.Equals(t.TargetVmId?.Trim(), vmId, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 1)
        {
            return new EdgePlacementDecision(EdgePlacementKind.Direct, matches[0], []);
        }
        if (matches.Count > 1)
        {
            return new EdgePlacementDecision(
                EdgePlacementKind.Ambiguous,
                null,
                [$"hay {matches.Count} túneles Direct para la VM '{vmId}'"]);
        }
        // Sin túnel central registrado, o con una fila previa a F13.11 sin TargetVmId, no se SABE que la VM
        // sea otra: fallar aquí tumbaría todos los deploys que hoy funcionan (R1/R2). Solo se falla con
        // evidencia de que la VM no es la del proxy central; sin ella, el camino de hoy más el aviso.
        if (string.IsNullOrWhiteSpace(centralProxyVmId))
        {
            return new EdgePlacementDecision(EdgePlacementKind.Central, null, [MissingDirectTunnelWarning]);
        }
        if (string.Equals(centralProxyVmId.Trim(), vmId, StringComparison.Ordinal))
        {
            return new EdgePlacementDecision(EdgePlacementKind.Central, null, []);
        }

        return new EdgePlacementDecision(
            EdgePlacementKind.Failed,
            null,
            [MissingDirectTunnelWarning],
            $"La VM '{vmId}' no es la VM del túnel CentralProxy ('{centralProxyVmId.Trim()}') y no tiene túnel Direct.");
    }

    public static DirectRouteValidationResult ValidateDirect(IEnumerable<DirectRouteCandidate> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var errors = new List<string>();
        foreach (var service in services.Where(s => s.RequiresPublicRoute))
        {
            var name = string.IsNullOrWhiteSpace(service.ServiceName)
                ? "<servicio sin nombre>"
                : service.ServiceName.Trim();

            if (string.IsNullOrWhiteSpace(service.ResolvedHost))
            {
                errors.Add($"{name}: tiene ruta pública pero no tiene hostname resuelto");
            }

            foreach (var prefix in service.PathPrefixes ?? [])
            {
                if (!string.Equals(prefix?.Trim(), "/", StringComparison.Ordinal))
                {
                    errors.Add($"{name}: Direct solo admite PathPrefix '/' (recibido '{prefix}')");
                }
            }
        }

        return new DirectRouteValidationResult(errors.Count == 0, errors);
    }
}
