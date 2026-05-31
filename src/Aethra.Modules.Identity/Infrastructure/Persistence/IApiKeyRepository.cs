using Aethra.Modules.Identity.Domain;

namespace Aethra.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Repositorio de <see cref="ApiKey"/>. El handler de auth usa <see cref="FindByHashAsync"/>
/// con tracking deshabilitado para el camino caliente (validar y emitir un ClaimsPrincipal);
/// los handlers de management usan <see cref="GetByIdAsync"/> con tracking habilitado.
/// </summary>
public interface IApiKeyRepository
{
    /// <summary>Lookup por hash determinístico — usado por el handler de auth.</summary>
    Task<ApiKey?> FindByHashAsync(byte[] hash, CancellationToken ct);

    /// <summary>Lookup por id — usado para revoke/inspect.</summary>
    Task<ApiKey?> GetByIdAsync(ApiKeyId id, CancellationToken ct);

    /// <summary>Lista todas las API keys (sin secret). Para la UI de management.</summary>
    Task<IReadOnlyList<ApiKey>> ListAllAsync(CancellationToken ct);
}
