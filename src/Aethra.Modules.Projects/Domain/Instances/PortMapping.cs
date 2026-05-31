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
public sealed record PortMapping(Port ContainerPort, int? HostPort, PortProtocol Protocol = PortProtocol.Tcp);
