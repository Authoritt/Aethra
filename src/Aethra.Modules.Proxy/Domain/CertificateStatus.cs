namespace Aethra.Modules.Proxy.Domain;

/// <summary>
/// Ciclo de vida de un <see cref="Certificate"/>:
/// <list type="bullet">
///   <item><term>Pending</term><description>Solicitud creada, esperando validación ACME.</description></item>
///   <item><term>Issued</term><description>Emitido y montable por YARP.</description></item>
///   <item><term>Failed</term><description>El último intento de emisión/renovación falló; ver <c>LastError</c>.</description></item>
///   <item><term>Renewing</term><description>Renovación en curso (cert anterior sigue siendo válido).</description></item>
/// </list>
/// </summary>
public enum CertificateStatus
{
    Pending = 0,
    Issued = 1,
    Failed = 2,
    Renewing = 3,
}
