using Aethra.Shared.Contracts.Authentication;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// Nombres de esquemas de autenticación. Centralizado para que módulos los referencien
/// sin acoplarse a la configuración del host.
/// </summary>
public static class AuthSchemes
{
    public const string Cookie = "aethra.cookie";
    public const string ApiKey = "aethra.apikey";
    // Alias de las constantes de Shared.Contracts: CookieSessionValidator (modulo Identity) usa
    // esas; si divergieran, la rama bootstrap y la del stamp leerian claims distintos.
    public const string BootstrapClaim = ApiKeyAuthSchemes.BootstrapClaim;
    public const string SecurityStampClaim = ApiKeyAuthSchemes.SecurityStampClaim;
}
