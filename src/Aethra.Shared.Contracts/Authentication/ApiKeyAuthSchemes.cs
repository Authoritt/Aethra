namespace Aethra.Shared.Contracts.Authentication;

/// <summary>
/// Constantes del esquema de auth de API keys. El nombre real del scheme está en
/// <c>Aethra.Api.Bootstrap.AuthSchemes.ApiKey</c> y se pasa al handler durante el
/// registro — esta clase solo expone los nombres de claims y headers que el handler
/// produce/consume, para que otros módulos los referencien sin acoplarse al host ni
/// al ensamblado de <c>Aethra.Modules.Identity</c>.
/// </summary>
public static class ApiKeyAuthSchemes
{
    /// <summary>Cabecera HTTP que lleva el bearer token. Convención: <c>Authorization: Bearer aethra_...</c>.</summary>
    public const string AuthorizationHeader = "Authorization";

    /// <summary>Prefijo Bearer esperado en el header Authorization.</summary>
    public const string BearerPrefix = "Bearer ";

    /// <summary>Claim type que el handler usa para emitir cada scope de la key.</summary>
    public const string ScopeClaim = "scope";

    /// <summary>Claim type donde se guarda el id de la API key (también usado como Subject).</summary>
    public const string ApiKeyIdClaim = "aethra:api_key_id";

    /// <summary>
    /// Nombre del scheme cookie que el host registra. Endpoints sensibles (gestión de
    /// api-keys mismas) lo referencian para forzar auth por cookie y rechazar API keys.
    /// Debe coincidir con la constante del host (<c>Aethra.Api.Bootstrap.AuthSchemes.Cookie</c>).
    /// </summary>
    public const string CookieScheme = "aethra.cookie";

    /// <summary>
    /// Scope wildcard. Equivale a tener todos los scopes — una API key con este claim,
    /// o una sesión cookie autenticada, pasa cualquier policy <c>scope:&lt;name&gt;</c>.
    /// Replica de <c>Aethra.Modules.Identity.Domain.ApiKey.AdminScope</c> para que módulos
    /// externos no dependan del aggregate.
    /// </summary>
    public const string AdminScope = "*";
}
