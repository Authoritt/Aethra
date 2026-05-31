namespace Aethra.Shared.Contracts.Monitoring;

/// <summary>
/// Evento cross-module: el monitor con id <see cref="MonitorId"/> cambió de estado tras un check.
/// Consumido por:
/// <list type="bullet">
///   <item><c>DashboardHub</c> para hacer push al frontend (sin necesidad de polling).</item>
///   <item><c>Notes</c> (F6+) para registrar incidentes en la línea de tiempo de la app.</item>
/// </list>
///
/// <para>
/// Los códigos de estado se transmiten como string (Up/Down/Degraded/Unknown) para no acoplar
/// los suscriptores al enum del módulo emisor: contratos = datos planos.
/// </para>
/// </summary>
public sealed record MonitorStatusChangedIntegrationEvent(
    string MonitorId,
    string From,
    string To,
    string CheckId,
    int? HttpStatusCode,
    int? LatencyMs,
    DateTimeOffset Timestamp) : IntegrationEvent;
