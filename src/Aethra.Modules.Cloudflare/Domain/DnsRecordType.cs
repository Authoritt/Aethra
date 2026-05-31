namespace Aethra.Modules.Cloudflare.Domain;

/// <summary>
/// Tipos de DNS record soportados por el modulo. Subconjunto de los tipos de Cloudflare;
/// se ampliara cuando aparezca demanda real para SRV, NS, CAA, etc.
/// </summary>
public enum DnsRecordType
{
    A = 1,
    AAAA = 2,
    CNAME = 3,
    TXT = 4,
    MX = 5,
}
