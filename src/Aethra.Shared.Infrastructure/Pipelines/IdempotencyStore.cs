namespace Aethra.Shared.Infrastructure.Pipelines;

/// <summary>
/// Almacen para resultados de comandos idempotentes. Implementacion concreta en cada modulo:
/// EF Core contra tabla shared.idempotency_keys con (key, request_type, response_json, expires_at).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Retorna la respuesta cacheada si existe y aun no expira, o null si es un comando nuevo.
    /// </summary>
    Task<string?> TryGetAsync(string key, string requestType, CancellationToken ct);

    /// <summary>
    /// Persiste el resultado serializado para que reintentos con la misma key devuelvan lo mismo.
    /// </summary>
    Task SaveAsync(string key, string requestType, string responseJson, TimeSpan ttl, CancellationToken ct);
}
