namespace Aethra.Shared.Contracts.Deployments;

/// <summary>
/// Se ha encolado un deploy para una Application. Disparado por webhook o trigger manual.
/// Consumido por:
/// - Monitoring: pausa el monitor mientras dura el deploy.
/// - DeployWorker (en el mismo módulo): inicia el build.
/// - Notes: agrega entrada a la línea de tiempo.
/// </summary>
public sealed record DeployRequestedEvent(
    string DeployJobId,
    string ApplicationId,
    string GitSha,
    string Trigger,           // "webhook" | "manual" | "scheduled"
    string? TriggeredBy       // userId o nombre de API key
) : IntegrationEvent;
