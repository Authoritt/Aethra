namespace Aethra.Api.Bootstrap;

/// <summary>
/// Nombres de esquemas de autenticación. Centralizado para que módulos los referencien
/// sin acoplarse a la configuración del host.
/// </summary>
public static class AuthSchemes
{
    public const string Cookie = "aethra.cookie";
    public const string ApiKey = "aethra.apikey";
}
