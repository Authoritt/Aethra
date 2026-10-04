namespace Aethra.Modules.Cloudflare.Domain;

/// <summary>
/// Modo de publicacion de un Cloudflare Tunnel gestionado por Aethra.
/// </summary>
public enum TunnelMode
{
    /// <summary>Camino actual: Cloudflare Tunnel termina en el proxy YARP del central.</summary>
    CentralProxy = 0,

    /// <summary>El tunnel connector vive junto a los servicios upstream en una VM satelite.</summary>
    Direct = 1,
}
