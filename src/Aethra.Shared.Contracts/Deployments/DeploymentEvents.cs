namespace Aethra.Shared.Contracts.Deployments;

/// <summary>
/// Evento cross-module: un deployment terminó OK y el contenedor nuevo está sirviendo tráfico.
/// El consumidor principal es el módulo <c>Proxy</c>, que actualiza la <c>Route</c> YARP para
/// apuntar al nuevo backend (atomic swap). Otros suscriptores potenciales: <c>Monitoring</c>
/// (programar un check post-deploy) y <c>Notes</c> (registrar en la timeline de la app).
///
/// <para>
/// <see cref="ContainerName"/> y <see cref="ContainerPort"/> son nullables porque algunos
/// deployments son headless (workers sin endpoint HTTP) y no implican cambio de routing.
/// </para>
/// </summary>
public sealed record DeploymentCompletedIntegrationEvent(
    string DeploymentId,
    string InstanceId,
    string NewImageRef,
    string? ContainerName,
    int? ContainerPort,
    DateTimeOffset CompletedAt) : IntegrationEvent;

/// <summary>
/// Evento cross-module: un deployment falló y, si aplicaba, se intentó rollback. Útil para que
/// la UI/Notifier emita la alerta correspondiente sin necesidad de polling.
/// </summary>
public sealed record DeploymentFailedIntegrationEvent(
    string DeploymentId,
    string InstanceId,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset FailedAt) : IntegrationEvent;

/// <summary>
/// Evento cross-module: un deployment falló pero el rollback restauró exitosamente el
/// contenedor previo. Útil para que el módulo Notifications avise al operador (rollback
/// implica downtime cero pero merece atención).
/// </summary>
public sealed record DeploymentRolledBackIntegrationEvent(
    string DeploymentId,
    string InstanceId,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset RolledBackAt) : IntegrationEvent;
