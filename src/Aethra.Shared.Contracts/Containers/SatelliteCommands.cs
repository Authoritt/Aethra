namespace Aethra.Shared.Contracts.Containers;

/// <summary>
/// Mensajes RPC del canal central → satélite que envuelven los specs de
/// <see cref="BuildSpec"/>, <see cref="RunSpec"/>, etc. con un <c>CorrelationId</c>
/// que permite al central correlacionar la respuesta del satélite con el job que la originó.
/// </summary>
/// <param name="CorrelationId">Identificador único del job/request, generado por el central.</param>
/// <param name="Spec">Spec de build agnóstico del runtime.</param>
/// <param name="PushTo">Credenciales y server al que pushear la imagen tras buildear, o null para no pushear.</param>
public sealed record BuildImageRequest(string CorrelationId, BuildSpec Spec, RegistryAuth? PushTo);

/// <summary>Respuesta del satélite al <see cref="BuildImageRequest"/>.</summary>
public sealed record BuildImageResponse(string CorrelationId, BuildResult Result);

/// <param name="CorrelationId">Identificador único del job/request.</param>
/// <param name="Spec">Spec de ejecución del contenedor.</param>
/// <param name="PullFrom">Credenciales para pullear la imagen si no está local.</param>
public sealed record RunContainerRequest(string CorrelationId, RunSpec Spec, RegistryAuth? PullFrom);

/// <summary>Respuesta del satélite al <see cref="RunContainerRequest"/>.</summary>
public sealed record RunContainerResponse(string CorrelationId, RunResult Result);

/// <param name="CorrelationId">Identificador único del job/request.</param>
/// <param name="ContainerNameOrId">Nombre o ID del contenedor a detener.</param>
public sealed record StopContainerRequest(string CorrelationId, string ContainerNameOrId);

/// <param name="CorrelationId">Identificador único del job/request.</param>
/// <param name="ContainerNameOrId">Nombre o ID del contenedor a eliminar.</param>
/// <param name="Force">Si true, fuerza la eliminación aunque el contenedor esté corriendo.</param>
public sealed record RemoveContainerRequest(string CorrelationId, string ContainerNameOrId, bool Force);

/// <param name="CorrelationId">Identificador único del job/request.</param>
/// <param name="ContainerNameOrId">Nombre o ID del contenedor cuyos logs streamear.</param>
/// <param name="TailLines">Número de líneas de historia a enviar antes de empezar a seguir (tail).</param>
public sealed record StreamLogsRequest(string CorrelationId, string ContainerNameOrId, int TailLines);

/// <summary>Frame individual del stream de logs.</summary>
/// <param name="CorrelationId">Mismo correlation id que el <see cref="StreamLogsRequest"/> original.</param>
/// <param name="Timestamp">Momento en que el satélite recibió/emitió la línea.</param>
/// <param name="Line">Texto de la línea (sin el separador <c>\n</c> final).</param>
public sealed record LogChunk(string CorrelationId, DateTimeOffset Timestamp, string Line);

/// <param name="CorrelationId">Identificador único del job/request.</param>
public sealed record ListContainersRequest(string CorrelationId);

/// <summary>Respuesta del satélite al <see cref="ListContainersRequest"/> con la lista de contenedores conocidos.</summary>
public sealed record ListContainersResponse(string CorrelationId, IReadOnlyList<ContainerInfo> Containers);
