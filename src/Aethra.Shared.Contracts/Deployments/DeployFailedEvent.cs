namespace Aethra.Shared.Contracts.Deployments;

/// <summary>
/// Un deploy falló. El contenedor anterior (si existía) sigue sirviendo tráfico — no hubo swap.
/// </summary>
public sealed record DeployFailedEvent(
    string DeployJobId,
    string ApplicationId,
    string GitSha,
    string ErrorCode,
    string ErrorMessage,
    string FailedStage         // "clone" | "build" | "healthcheck" | "migrations" | "swap"
) : IntegrationEvent;
