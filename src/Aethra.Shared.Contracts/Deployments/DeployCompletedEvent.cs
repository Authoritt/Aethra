namespace Aethra.Shared.Contracts.Deployments;

/// <summary>
/// Un deploy terminó exitosamente. El nuevo contenedor está sirviendo tráfico.
/// </summary>
public sealed record DeployCompletedEvent(
    string DeployJobId,
    string ApplicationId,
    string GitSha,
    string ContainerName,
    string ImageTag,
    string ContainerHost,
    int ContainerPort,
    TimeSpan Duration
) : IntegrationEvent;
