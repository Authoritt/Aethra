namespace Aethra.Shared.Contracts.Settings;

/// <summary>
/// Read-model cross-module: permite que otros módulos (Cloudflare, Github webhooks, registry
/// interno) obtengan el valor de una credencial externa por nombre sin acoplarse a la
/// persistencia ni al codec del módulo Settings.
///
/// Convención de nombres: <c>"namespace:slug"</c> (ej. <c>cloudflare:default</c>, <c>registry:internal</c>).
/// El namespace agrupa por tipo de proveedor; el slug discrimina entre múltiples credenciales
/// del mismo tipo.
/// </summary>
public interface IIntegrationCredentialResolver
{
    /// <summary>
    /// Devuelve el secreto en texto plano si la credencial existe; <c>null</c> en caso contrario.
    /// La implementación es responsable de descifrar el blob con el codec de DataProtection.
    /// No se debe loguear el valor devuelto.
    /// </summary>
    Task<string?> GetSecretAsync(string credentialName, CancellationToken ct);

    /// <summary>
    /// Indica si la credencial existe sin descifrarla. Útil para validar configuraciones
    /// (ej. en endpoints de health/config) sin exponer el secreto.
    /// </summary>
    Task<bool> ExistsAsync(string credentialName, CancellationToken ct);
}
