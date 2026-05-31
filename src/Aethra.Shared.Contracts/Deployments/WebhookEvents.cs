namespace Aethra.Shared.Contracts.Deployments;

/// <summary>
/// Una transición de estado de un deploy. Publicada por el módulo Deployments para que:
/// - DashboardHub la reenvíe al frontend (UI live).
/// - Monitoring pause/reanude el monitor uptime del app correspondiente.
/// - Notes inserte una entrada en la línea de tiempo de la app.
/// </summary>
public sealed record DeployStatusChangedEvent(
    string JobId,
    string ApplicationId,
    string PreviousStatus,
    string NewStatus,
    DateTimeOffset At) : IntegrationEvent;

public sealed record DeployLogAppendedEvent(
    string JobId,
    long Sequence,
    DateTimeOffset Timestamp,
    string Level,
    string Stage,
    string Text) : IntegrationEvent;
