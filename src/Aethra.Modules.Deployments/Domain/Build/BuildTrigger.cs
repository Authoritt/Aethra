namespace Aethra.Modules.Deployments.Domain.Build;

/// <summary>
/// Origen del build. <c>Webhook</c> = recibido por <c>POST /webhooks/git</c> tras un push;
/// <c>Manual</c> = disparado por el operador desde la UI/API; <c>Schedule</c> = ejecutado
/// por un cron interno (no usado todavía, reservado para F9.5+).
/// </summary>
public enum BuildTrigger
{
    Webhook = 0,
    Manual = 1,
    Schedule = 2,
}
