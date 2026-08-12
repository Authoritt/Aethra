using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Projects.Domain.Instances;

/// <summary>
/// Protocolo de un mapeo de puertos.
/// </summary>
public enum PortProtocol
{
    Tcp = 0,
    Udp = 1,
}

/// <summary>
/// Mapeo de un puerto del contenedor al host de la VM target.
///
/// <c>ContainerPort</c>: puerto expuesto por el proceso dentro del contenedor.
/// <c>HostPort</c>: puerto en el host. Si es <c>null</c>, YARP llega al contenedor por la red
/// Docker interna sin publicar puerto al host.
/// </summary>
/// <remarks>
/// Sealed record (no record struct): la colección de puertos se persistirá como JSON column
/// dentro de la <see cref="Instance"/> (no como tabla aparte). Un record struct genera código
/// equivalente pero el patrón del repo (ver <c>BuildArg</c>, <c>VolumeMount</c> originales) usa
/// sealed record para VOs serializables. Igualdad por valor incluida automáticamente.
/// </remarks>
public sealed record PortMapping(Port ContainerPort, int? HostPort, PortProtocol Protocol = PortProtocol.Tcp)
{
    /// <summary>Rango válido de un puerto TCP/UDP. El 0 no vale: significa "elige tú".</summary>
    public const int MinPort = 1;

    /// <inheritdoc cref="MinPort"/>
    public const int MaxPort = 65535;

    /// <summary>
    /// ¿Es válido este puerto de host? <c>null</c> significa "no se publica al host" y es válido.
    ///
    /// <para>La comprobación NO va en el constructor, y es deliberado. EF materializa este record
    /// por su constructor posicional al leer de la base: una guarda ahí haría que cualquier fila
    /// escrita por el código anterior —con un cero, un negativo o un valor por encima de 65535, que
    /// antes se aceptaban— reventara al LEERLA. El usuario recibiría un 500 al listar sus instancias
    /// y no podría ni siquiera corregir la configuración, porque para corregirla hay que poder
    /// leerla. Un invariante nuevo sobre datos viejos se aplica en el borde de entrada o se migra;
    /// nunca en la materialización.</para>
    /// </summary>
    public static bool IsValidHostPort(int? hostPort)
        => hostPort is not { } hp || (hp >= MinPort && hp <= MaxPort);

    /// <summary>
    /// Convierte el nombre de un protocolo al valor del enum. Devuelve <c>false</c> si no es uno de
    /// los soportados.
    ///
    /// <para>Existe porque el mapeo anterior era <c>protocolo == "tcp" ? Tcp : Udp</c>: cualquier
    /// otra cosa —un typo como <c>"tpc"</c>, un protocolo no soportado, una cadena vacía— se
    /// aceptaba silenciosamente como UDP. El usuario pedía un transporte y obtenía otro, sin aviso.
    /// Convertir una entrada inválida en una válida distinta es peor que rechazarla.</para>
    /// </summary>
    public static bool TryParseProtocol(string? value, out PortProtocol protocol)
    {
        protocol = PortProtocol.Tcp;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var trimmed = value.Trim();
        if (string.Equals(trimmed, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            protocol = PortProtocol.Tcp;
            return true;
        }
        if (string.Equals(trimmed, "udp", StringComparison.OrdinalIgnoreCase))
        {
            protocol = PortProtocol.Udp;
            return true;
        }
        return false;
    }

    /// <summary>Los protocolos aceptados, para mensajes de error y validadores.</summary>
    public static IReadOnlyList<string> SupportedProtocols { get; } = ["tcp", "udp"];
}
