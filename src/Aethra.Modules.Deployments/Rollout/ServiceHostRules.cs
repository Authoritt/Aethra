using Aethra.Shared.Contracts.Projects;

namespace Aethra.Modules.Deployments.Rollout;

/// <param name="FromOverride">
/// OT-009 — <c>true</c> si el host salió del override por instancia (no del template).
/// </param>
public sealed record ServiceHostResolution(
    string? Host,
    bool UsesDomainToken,
    bool DomainResolved,
    bool FromOverride = false);

/// <summary>
/// OT-009 — un servicio del template que publica rutas: su host EFECTIVO y los prefijos que sirve.
/// </summary>
public sealed record ServicePublicEndpoint(
    TemplateServiceView Service,
    string Host,
    IReadOnlyList<string> PathPrefixes,
    bool FromOverride);

/// <summary>
/// OT-008 F1 — resolución pura del host público que T5 insertará en
/// <c>NativeDeployRunner.ReconcileRoutingAsync</c> donde hoy se elige <c>svc.Hostname ?? instanceHostname</c>
/// (línea histórica 606).
///
/// <para>
/// OT-009 — ÚNICO punto de resolución del hostname efectivo de un servicio:
/// <c>override de la instancia ?? svc.Hostname interpolado ({domain}) ?? host de la instancia</c>.
/// Lo consumen las rutas del proxy, el ingress Direct, el DNS de Cloudflare y los monitores
/// (todos derivan del mismo <see cref="PlanPublicEndpoints"/>), y la validación Direct. Sin override
/// la salida es byte-idéntica a la histórica.
/// </para>
/// </summary>
public static class ServiceHostRules
{
    public const string DomainToken = "{domain}";

    public static ServiceHostResolution Resolve(string? serviceHostname, string? instanceHostname)
    {
        var instanceHost = Normalize(instanceHostname);
        var serviceHost = Normalize(serviceHostname);
        if (serviceHost is null)
        {
            return new ServiceHostResolution(instanceHost, UsesDomainToken: false, DomainResolved: true);
        }

        var usesDomain = serviceHost.Contains(DomainToken, StringComparison.Ordinal);
        if (!usesDomain)
        {
            return new ServiceHostResolution(serviceHost, UsesDomainToken: false, DomainResolved: true);
        }

        if (instanceHost is null)
        {
            return new ServiceHostResolution(null, UsesDomainToken: true, DomainResolved: false);
        }

        return new ServiceHostResolution(
            serviceHost.Replace(DomainToken, instanceHost, StringComparison.Ordinal),
            UsesDomainToken: true,
            DomainResolved: true);
    }

    /// <summary>
    /// OT-009 — host efectivo: el override de la instancia para <paramref name="serviceName"/> gana;
    /// sin override, exactamente <see cref="Resolve"/>.
    /// </summary>
    public static ServiceHostResolution ResolveEffective(
        string serviceName,
        string? serviceHostname,
        string? instanceHostname,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var overridden = FindOverride(serviceName, overrides);
        return overridden is not null
            ? new ServiceHostResolution(overridden, UsesDomainToken: false, DomainResolved: true, FromOverride: true)
            : Resolve(serviceHostname, instanceHostname);
    }

    /// <summary>
    /// OT-009 — el servicio tiene host PROPIO (del template o por override de la instancia). Un host
    /// propio sin <c>PathPrefixes</c> implica <c>"/"</c> (quiere ruta pública).
    /// </summary>
    public static bool HasOwnHost(TemplateServiceView service, IReadOnlyDictionary<string, string>? overrides)
    {
        ArgumentNullException.ThrowIfNull(service);
        return !string.IsNullOrWhiteSpace(service.Hostname) || FindOverride(service.Name, overrides) is not null;
    }

    /// <summary>
    /// Prefijos públicos del servicio. Histórico (<c>NativeDeployRunner.ResolvePublicPrefixes</c>):
    /// los declarados; si no hay y el servicio tiene host propio, <c>"/"</c>; si no, ninguno (interno).
    /// </summary>
    public static IReadOnlyList<string> PublicPrefixes(
        TemplateServiceView service,
        IReadOnlyDictionary<string, string>? overrides)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.PathPrefixes.Count > 0
            ? service.PathPrefixes
            : (HasOwnHost(service, overrides) ? ["/"] : service.PathPrefixes);
    }

    /// <summary>
    /// OT-009 — plan de publicación: un elemento por servicio con host efectivo y al menos un prefijo,
    /// en el orden del template. Rutas, ingress, DNS y monitores del runner salen TODOS de aquí.
    /// </summary>
    public static IReadOnlyList<ServicePublicEndpoint> PlanPublicEndpoints(
        IReadOnlyList<TemplateServiceView> services,
        string? instanceHostname,
        IReadOnlyDictionary<string, string>? overrides)
    {
        ArgumentNullException.ThrowIfNull(services);
        var plan = new List<ServicePublicEndpoint>();
        foreach (var svc in services)
        {
            var resolution = ResolveEffective(svc.Name, svc.Hostname, instanceHostname, overrides);
            var host = resolution.Host;
            if (string.IsNullOrWhiteSpace(host))
            {
                continue;
            }
            var prefixes = PublicPrefixes(svc, overrides);
            if (prefixes.Count == 0)
            {
                continue;
            }
            plan.Add(new ServicePublicEndpoint(svc, host, prefixes, resolution.FromOverride));
        }
        return plan;
    }

    /// <summary>
    /// OT-009 — overrides que no casan con ningún servicio del template (servicio renombrado o
    /// retirado): no se aplican, y el deploy lo avisa en vez de callarlo.
    /// </summary>
    public static IReadOnlyList<string> UnmatchedOverrides(
        IReadOnlyList<TemplateServiceView> services,
        IReadOnlyDictionary<string, string>? overrides)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (overrides is null || overrides.Count == 0)
        {
            return [];
        }
        return [.. overrides.Keys
            .Where(k => !services.Any(s => string.Equals(s.Name?.Trim(), k?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)];
    }

    private static string? FindOverride(string? serviceName, IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0 || string.IsNullOrWhiteSpace(serviceName))
        {
            return null;
        }
        var name = serviceName.Trim();
        foreach (var (key, value) in overrides)
        {
            if (string.Equals(key?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return Normalize(value)?.ToLowerInvariant();
            }
        }
        return null;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
