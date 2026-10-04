namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// Eventos de integración del ciclo de vida de <c>Instance</c> emitidos por el módulo Projects.
///
/// El módulo Proxy suscribe estos eventos para mantener su tabla <c>Routes</c> en sync con el
/// hostname auto-derived (subdominio del BaseDomain) o el customDomain explícito. El módulo
/// Cloudflare (F9.6) suscribe los Custom*Events para crear/eliminar registros DNS.
/// </summary>
public sealed record InstanceProvisionedIntegrationEvent(
    string InstanceId,
    string TemplateId,
    string ClientId,
    string Environment,
    string TargetVmId,
    string ContainerName,
    int? PrimaryPort,
    string? AutoHostname,
    string? CustomDomain,
    DateTimeOffset CreatedAt) : IntegrationEvent;

/// <summary>
/// Se publica cuando una <c>Instance</c> se borra de la BD. Dispara el teardown COMPLETO en los
/// módulos consumidores: Proxy elimina TODAS sus Routes, Cloudflare borra el/los DNS del hostname,
/// Monitoring borra el/los monitor(es) de la instance, y Deployments elimina los contenedores en
/// el satélite (<see cref="TargetVmId"/> + <see cref="ContainerNames"/>). Los dos últimos campos son
/// opcionales (eventos viejos en outbox deserializan a null) — sin ellos el teardown de contenedor
/// se omite (best-effort).
/// </summary>
public sealed record InstanceRemovedIntegrationEvent(
    string InstanceId,
    string? AutoHostname,
    string? CustomDomain,
    DateTimeOffset RemovedAt,
    string? TargetVmId = null,
    IReadOnlyList<string>? ContainerNames = null,
    // OT-009 — hosts fijados por servicio EN esta instancia (overrides). Son exclusivos de la
    // instancia, así que su DNS y sus rutas se limpian con ella. null en eventos viejos = ninguno.
    IReadOnlyList<string>? ServiceHostnames = null) : IntegrationEvent;

/// <summary>
/// Se publica cuando el operador define o cambia el dominio custom de una <c>Instance</c>.
/// El módulo Cloudflare (F9.6) crea el CNAME y el Proxy genera/actualiza la Route TLS.
/// </summary>
public sealed record CustomDomainRequestedIntegrationEvent(
    string InstanceId,
    string Hostname,
    string? CloudflareZoneId,
    string TargetVmId,
    int? PrimaryPort,
    DateTimeOffset RequestedAt) : IntegrationEvent;

/// <summary>
/// Se publica cuando el operador limpia el dominio custom de una <c>Instance</c> (vuelve a
/// usar el auto-hostname). El Proxy debe quitar la Route TLS asociada al hostname custom.
/// </summary>
public sealed record CustomDomainRemovedIntegrationEvent(
    string InstanceId,
    string Hostname,
    DateTimeOffset RemovedAt) : IntegrationEvent;

/// <summary>
/// OT-009 — se publica cuando el operador fija, cambia o quita el hostname público de UN servicio
/// en una <c>Instance</c> (override del <c>svc.Hostname</c> del template). El host reconcilia el
/// routing nativo de la instancia (rutas, ingress, DNS, monitor) igual que con el dominio custom.
/// <paramref name="Hostname"/> null = override quitado (vuelve al host del template).
/// </summary>
public sealed record InstanceServiceHostnameChangedIntegrationEvent(
    string InstanceId,
    string ServiceName,
    string? Hostname,
    string? PreviousHostname,
    DateTimeOffset ChangedAt) : IntegrationEvent;
