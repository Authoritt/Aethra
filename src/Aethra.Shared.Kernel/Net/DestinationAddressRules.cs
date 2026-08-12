using System.Net;
using System.Net.Sockets;

namespace Aethra.Shared.Kernel.Net;

/// <summary>Por qué una dirección de destino se considera insegura para una petición del servidor.</summary>
public enum DestinationRisk
{
    /// <summary>Dirección enrutable de internet: sin objeción.</summary>
    None = 0,

    /// <summary>La propia máquina (<c>127.0.0.0/8</c>, <c>::1</c>). Alcanza servicios sin exponer.</summary>
    Loopback = 1,

    /// <summary>Red interna (<c>10/8</c>, <c>172.16/12</c>, <c>192.168/16</c>, <c>fc00::/7</c>).</summary>
    Private = 2,

    /// <summary>
    /// Link-local (<c>169.254/16</c>, <c>fe80::/10</c>). Incluye <c>169.254.169.254</c>, el endpoint
    /// de metadatos de las nubes: el destino más valioso de un SSRF, porque suele entregar
    /// credenciales de instancia sin autenticación.
    /// </summary>
    LinkLocal = 3,

    /// <summary>CGNAT (<c>100.64/10</c>). Es el rango que usa Tailscale, o sea la malla privada.</summary>
    CarrierGrade = 4,

    /// <summary>Sin especificar (<c>0.0.0.0</c>, <c>::</c>): en muchas pilas equivale a loopback.</summary>
    Unspecified = 5,

    /// <summary>Multicast o broadcast: no es un destino unicast legítimo.</summary>
    NonUnicast = 6,

    /// <summary>Reservada por IANA o de uso especial (documentación, benchmarking, etc.).</summary>
    Reserved = 7,
}

/// <summary>
/// Clasifica una dirección IP como destino de una petición que <b>origina el servidor</b>.
///
/// <para>Aethra es un plano de control: corre en la misma red que los servicios que gestiona y tiene
/// alcance a la malla privada, al endpoint de metadatos de la nube y a los puertos que no están
/// publicados. Una función que acepte una URL del llamante y la pida desde aquí convierte al panel
/// en un proxy hacia todo eso. Por eso la decisión vive en un solo sitio: si cada superficie
/// (monitores, webhooks, clone de git) improvisara su propia lista, tendríamos criterios distintos y
/// el atacante solo necesita el más laxo.</para>
///
/// <para>Función pura sobre <see cref="IPAddress"/>: no resuelve DNS ni abre conexiones. Quien
/// resuelve el nombre decide qué hacer con cada dirección resultante — y debe consultarlas TODAS,
/// porque un nombre puede resolver a varias.</para>
/// </summary>
public static class DestinationAddressRules
{
    /// <summary>
    /// Clasifica la dirección. <see cref="DestinationRisk.None"/> significa "enrutable en internet",
    /// no "segura en abstracto": la política de arriba decide si además hay allowlist.
    /// </summary>
    public static DestinationRisk Classify(IPAddress? address)
    {
        if (address is null)
        {
            return DestinationRisk.Reserved;
        }

        // Una dirección IPv4 embebida en IPv6 (::ffff:127.0.0.1) es la misma máquina que su IPv4.
        // Sin desenvolverla, toda la clasificación de abajo se evalúa sobre la forma IPv6 y deja
        // pasar loopback y redes privadas: es el bypass clásico de estos filtros.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return DestinationRisk.Loopback;
        }
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return DestinationRisk.Unspecified;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => ClassifyIPv4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => ClassifyIPv6(address),
            // Ni IPv4 ni IPv6: no sabemos razonar sobre ella, así que no se permite.
            _ => DestinationRisk.Reserved,
        };
    }

    /// <summary>Atajo para el caso habitual: ¿se puede pedir a esta dirección sin más comprobaciones?</summary>
    public static bool IsPubliclyRoutable(IPAddress? address) => Classify(address) == DestinationRisk.None;

    /// <summary>Explicación corta y sin jerga para el mensaje de error que verá quien configuró la URL.</summary>
    public static string Describe(DestinationRisk risk) => risk switch
    {
        DestinationRisk.None => "dirección pública",
        DestinationRisk.Loopback => "la propia máquina (loopback)",
        DestinationRisk.Private => "una red privada interna",
        DestinationRisk.LinkLocal => "una dirección link-local o el endpoint de metadatos de la nube",
        DestinationRisk.CarrierGrade => "la red privada de la malla (CGNAT)",
        DestinationRisk.Unspecified => "una dirección sin especificar",
        DestinationRisk.NonUnicast => "una dirección multicast o de difusión",
        _ => "una dirección reservada",
    };

    private static DestinationRisk ClassifyIPv4(byte[] b)
    {
        // 10.0.0.0/8
        if (b[0] == 10)
        {
            return DestinationRisk.Private;
        }
        // 172.16.0.0/12
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
        {
            return DestinationRisk.Private;
        }
        // 192.168.0.0/16
        if (b[0] == 192 && b[1] == 168)
        {
            return DestinationRisk.Private;
        }
        // 169.254.0.0/16 — incluye 169.254.169.254 (metadatos de instancia).
        if (b[0] == 169 && b[1] == 254)
        {
            return DestinationRisk.LinkLocal;
        }
        // 100.64.0.0/10 — CGNAT, el rango de Tailscale.
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
        {
            return DestinationRisk.CarrierGrade;
        }
        // 0.0.0.0/8
        if (b[0] == 0)
        {
            return DestinationRisk.Unspecified;
        }
        // 224.0.0.0/4 multicast · 240.0.0.0/4 reservado · 255.255.255.255 broadcast
        if (b[0] >= 224 && b[0] <= 239)
        {
            return DestinationRisk.NonUnicast;
        }
        if (b[0] >= 240)
        {
            return b[0] == 255 && b[1] == 255 && b[2] == 255 && b[3] == 255
                ? DestinationRisk.NonUnicast
                : DestinationRisk.Reserved;
        }
        // 192.0.0.0/24 (IETF), 192.0.2.0/24, 198.51.100.0/24, 203.0.113.0/24 (documentación),
        // 198.18.0.0/15 (benchmarking): no son destinos reales de producción.
        if (b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2))
        {
            return DestinationRisk.Reserved;
        }
        if (b[0] == 198 && (b[1] == 51 && b[2] == 100))
        {
            return DestinationRisk.Reserved;
        }
        if (b[0] == 203 && b[1] == 0 && b[2] == 113)
        {
            return DestinationRisk.Reserved;
        }
        if (b[0] == 198 && (b[1] == 18 || b[1] == 19))
        {
            return DestinationRisk.Reserved;
        }
        return DestinationRisk.None;
    }

    private static DestinationRisk ClassifyIPv6(IPAddress address)
    {
        if (address.IsIPv6Multicast)
        {
            return DestinationRisk.NonUnicast;
        }
        if (address.IsIPv6LinkLocal)
        {
            return DestinationRisk.LinkLocal;
        }
        if (address.IsIPv6SiteLocal)
        {
            return DestinationRisk.Private;
        }
        // Unique local addresses: fc00::/7 (el primer octeto es 0xfc o 0xfd).
        var b = address.GetAddressBytes();
        if ((b[0] & 0xFE) == 0xFC)
        {
            return DestinationRisk.Private;
        }
        // 2001:db8::/32 — documentación.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
        {
            return DestinationRisk.Reserved;
        }
        // 64:ff9b::/96 (NAT64) y ::/96 (IPv4-compatible, obsoleto) pueden envolver una IPv4: si no
        // se desenvuelven, sirven como bypass igual que ::ffff:. Se tratan como reservadas.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B)
        {
            return DestinationRisk.Reserved;
        }
        return DestinationRisk.None;
    }
}
