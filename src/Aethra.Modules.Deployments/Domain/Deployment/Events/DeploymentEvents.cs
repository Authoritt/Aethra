using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Deployments.Domain.Deployment.Events;

/// <summary>
/// Evento de dominio in-module: un deployment acaba de encolarse contra una Instance específica.
/// El suscriptor cross-module equivalente vive en <c>Shared.Contracts.Deployments</c>; este sirve
/// solo para reactores intra-módulo.
/// </summary>
public sealed record DeploymentQueuedEvent(
    DeploymentId DeploymentId,
    string BuildId,
    string InstanceId,
    string NewImageRef) : DomainEvent;

/// <summary>
/// Evento de dominio in-module: el deployment cambió de estado. Lo usa el orquestador para
/// publicar telemetría/SignalR sin invocar consumidores externos.
/// </summary>
public sealed record DeploymentStatusChangedDomainEvent(
    DeploymentId DeploymentId,
    DeploymentStatus From,
    DeploymentStatus To) : DomainEvent;

/// <summary>
/// Evento de dominio in-module: el deployment completó con éxito y el contenedor nuevo está
/// sirviendo tráfico. El equivalente cross-module (<c>DeploymentCompletedIntegrationEvent</c>)
/// se emite desde el orquestador hacia el outbox para que el módulo Proxy actualice la Route.
/// </summary>
public sealed record DeploymentCompletedDomainEvent(
    DeploymentId DeploymentId,
    string InstanceId,
    string NewContainerId,
    string NewImageRef) : DomainEvent;

/// <summary>
/// Evento de dominio in-module: el deployment falló en alguna etapa. Si el rollback se ejecutó
/// con éxito, el estado final es <c>RolledBack</c> (no <c>Failed</c>) y este evento se emite
/// igual para que la UI muestre la causa raíz.
/// </summary>
public sealed record DeploymentFailedDomainEvent(
    DeploymentId DeploymentId,
    string InstanceId,
    DeploymentStatus FailedAtStage,
    string ErrorCode,
    string ErrorMessage) : DomainEvent;
