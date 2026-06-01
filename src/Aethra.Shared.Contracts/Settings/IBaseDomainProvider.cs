namespace Aethra.Shared.Contracts.Settings;

/// <summary>
/// Read-model cross-module: expone el base domain activo (único) para que otros módulos
/// (Proxy, Cloudflare, Deployments) puedan construir hostnames derivados sin duplicar la
/// configuración. Solo una <c>BaseDomain</c> puede estar activa a la vez.
/// </summary>
public interface IBaseDomainProvider
{
    /// <summary>
    /// Devuelve el base domain marcado como activo, o <c>null</c> si el operador no ha
    /// configurado ninguno aún.
    /// </summary>
    Task<BaseDomainInfo?> GetActiveAsync(CancellationToken ct);
}

/// <summary>
/// Proyección read-only del base domain activo. <see cref="CloudflareZoneId"/> es opcional
/// porque el módulo Settings puede registrar el hostname antes de que el operador conecte
/// la zona en Cloudflare. <see cref="WildcardConfigured"/> es un flag manual: el operador
/// confirma que el registro DNS <c>*.hostname</c> ya está creado.
/// </summary>
public sealed record BaseDomainInfo(string Hostname, string? CloudflareZoneId, bool WildcardConfigured);
