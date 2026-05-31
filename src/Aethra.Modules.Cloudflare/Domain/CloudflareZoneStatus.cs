namespace Aethra.Modules.Cloudflare.Domain;

/// <summary>
/// Estado de la zona segun reportado por la API de Cloudflare.
/// </summary>
public enum CloudflareZoneStatus
{
    /// <summary>Estado por defecto antes del primer sync.</summary>
    Unknown = 0,
    /// <summary>Zona activa, sirviendo DNS.</summary>
    Active = 1,
    /// <summary>Pendiente de verificacion (nameservers no apuntan a CF aun).</summary>
    Pending = 2,
    /// <summary>Suspendida por Cloudflare.</summary>
    Suspended = 3,
}
