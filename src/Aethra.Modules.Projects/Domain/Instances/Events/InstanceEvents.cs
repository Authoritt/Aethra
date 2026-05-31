using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain.Instances.Events;

/// <summary>
/// Disparado al crear una <see cref="Instance"/> (combinación Template + Client + Environment).
/// El módulo Deployments lo consume para programar el primer deploy si <c>AutoDeployOnNewBuild</c>
/// está activo y ya hay un build disponible.
/// </summary>
public sealed record InstanceCreatedEvent(
    InstanceId InstanceId,
    TemplateId TemplateId,
    ClientId ClientId,
    string Environment,
    string TargetVmId,
    string ContainerName) : DomainEvent;

/// <summary>
/// Disparado cuando cambian aspectos de runtime (VM target, puertos, volúmenes, healthcheck).
/// El siguiente deploy aplicará la nueva config.
/// </summary>
public sealed record InstanceRuntimeUpdatedEvent(
    InstanceId InstanceId,
    string TargetVmId) : DomainEvent;

/// <summary>
/// Disparado cuando se activa o desactiva el auto-deploy on new build.
/// </summary>
public sealed record InstanceAutoDeployChangedEvent(
    InstanceId InstanceId,
    bool Enabled) : DomainEvent;

/// <summary>
/// Disparado cuando se setea, cambia o limpia el dominio custom de una instance.
/// <c>CustomDomain == null</c> ⇒ vuelve al auto-hostname derivado de Settings.BaseDomain.
/// El módulo Proxy lo consume para reconfigurar rutas YARP y emitir/cambiar el certificado TLS.
/// </summary>
public sealed record InstanceCustomDomainChangedEvent(
    InstanceId InstanceId,
    string? CustomDomain) : DomainEvent;
