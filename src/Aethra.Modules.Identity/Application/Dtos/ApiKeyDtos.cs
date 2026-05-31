namespace Aethra.Modules.Identity.Application.Dtos;

/// <summary>
/// Resumen visible en la UI/list. NO incluye el secret — solo el prefijo identificador.
/// </summary>
public sealed record ApiKeySummaryDto(
    string Id,
    string Name,
    string KeyPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Resultado de crear una API key. <see cref="Secret"/> aparece UNA SOLA VEZ;
/// después solo existe el hash. La UI debe mostrar un banner "store this now".
/// </summary>
public sealed record CreateApiKeyResultDto(
    string Id,
    string Name,
    string KeyPrefix,
    string Secret,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>Texto informativo para la UI — no es localizable, es un hint para el frontend.</summary>
    public string SecretWarning => "Store this secret now — Aethra never displays it again.";
}
