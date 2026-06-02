namespace Aethra.Modules.Notifications.Domain;

/// <summary>
/// Catalogo de event types que el modulo Notifications escucha desde el bus de integration
/// events. Un <see cref="NotificationChannel"/> con <c>EventFilters</c> vacios escucha TODOS;
/// si declara filtros, solo dispara para los matches exactos.
///
/// Estos strings se persisten en la BD (columna jsonb / text[]) y se exponen en la UI para
/// que el operador active/desactive por canal.
/// </summary>
public static class NotificationEventTypes
{
    public const string MonitorDown = "monitor.down";
    public const string MonitorRecovered = "monitor.recovered";
    public const string BuildFailed = "build.failed";
    public const string DeploymentFailed = "deployment.failed";
    public const string DeploymentRolledBack = "deployment.rolled_back";
    public const string CertificateExpired = "cert.expired";
    public const string CertificateFailed = "cert.failed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        MonitorDown,
        MonitorRecovered,
        BuildFailed,
        DeploymentFailed,
        DeploymentRolledBack,
        CertificateExpired,
        CertificateFailed,
    };
}
