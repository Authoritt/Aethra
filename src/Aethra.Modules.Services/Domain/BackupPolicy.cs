namespace Aethra.Modules.Services.Domain;

/// <summary>
/// Politica de backup de un <see cref="ManagedService"/>: cron, retencion (cuantos backups
/// mantener) y destino donde escribir el dump.
///
/// <see cref="Destination"/> usa esquema URL:
/// <list type="bullet">
///   <item><c>volume://path/local</c> — disco local del central (montado en /var/lib/aethra/backups).</item>
///   <item><c>s3://bucket/prefix</c> — S3-compatible (lee credencial via IntegrationCredential <c>s3:default</c>).</item>
///   <item><c>satellite://&lt;vmId&gt;/sub</c> o <c>satellite://auto/sub</c> — disco de un satélite con espacio
///         libre ('auto' = el satélite Connected con más disco), para descargar el disco del central.</item>
/// </list>
/// </summary>
public sealed record BackupPolicy(
    string CronExpression,
    int RetentionCount,
    string Destination)
{
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(CronExpression)
        && RetentionCount > 0
        && !string.IsNullOrWhiteSpace(Destination)
        && (Destination.StartsWith("volume://", StringComparison.Ordinal)
            || Destination.StartsWith("s3://", StringComparison.Ordinal)
            || Destination.StartsWith("satellite://", StringComparison.Ordinal));
}
