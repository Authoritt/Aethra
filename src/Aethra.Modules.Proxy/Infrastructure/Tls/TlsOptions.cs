namespace Aethra.Modules.Proxy.Infrastructure.Tls;

/// <summary>
/// Configuración del subsistema TLS. Se enlaza desde la sección <c>"Tls"</c>:
/// <code>
/// "Tls": {
///   "AccountEmail": "admin@example.com",
///   "UseStaging": true,
///   "RenewBeforeDays": 30
/// }
/// </code>
/// </summary>
public sealed class TlsOptions
{
    public const string SectionName = "Tls";

    /// <summary>Email de contacto registrado en la cuenta ACME. Obligatorio.</summary>
    public string AccountEmail { get; set; } = string.Empty;

    /// <summary>Si <c>true</c> usa el directory de staging (no produce certs confiables, pero sin rate-limits).</summary>
    public bool UseStaging { get; set; } = true;

    /// <summary>Días antes de <c>NotAfter</c> en los que el cert se marca como renovable.</summary>
    public int RenewBeforeDays { get; set; } = 30;
}
