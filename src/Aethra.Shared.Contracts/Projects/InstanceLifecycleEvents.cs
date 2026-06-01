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
/// Se publica cuando una <c>Instance</c> se borra de la BD. El Proxy debe eliminar la Route
/// asociada para que YARP deje de aceptar tráfico al hostname.
/// </summary>
public sealed record InstanceRemovedIntegrationEvent(
    string InstanceId,
    string? AutoHostname,
    string? CustomDomain,
    DateTimeOffset RemovedAt) : IntegrationEvent;

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
