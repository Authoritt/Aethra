using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Cloudflare.Domain;

/// <summary>
/// Identificador interno de la zona Cloudflare gestionada por Aethra.
/// No confundir con <c>ZoneId</c> externo (el id que devuelve Cloudflare, hex de 32 chars).
/// </summary>
public readonly record struct CloudflareZoneId(AethraId Value)
{
    public static CloudflareZoneId New() => new(AethraId.NewId("cfz"));
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identificador interno de un DNS record gestionado por Aethra. El id externo de Cloudflare
/// queda almacenado aparte en <c>DnsRecord.ExternalRecordId</c>.
/// </summary>
public readonly record struct DnsRecordId(AethraId Value)
{
    public static DnsRecordId New() => new(AethraId.NewId("cfr"));
    public override string ToString() => Value.ToString();
}
