namespace Aethra.Shared.Contracts.Deployments;

// ---------------------------------------------------------------------------
// Comandos RPC central → satélite.
//
// Estos records NO son integration events: son DTOs de invocación remota
// que viajan por SignalR (canal inverso). Por eso NO heredan de IntegrationEvent
// ni implementan INotification — son payloads de InvokeAsync/Result.
//
// Ubicación en Shared.Contracts para que el central (que invoca) y el satélite
// (que ejecuta) compartan el mismo contrato sin acoplarse a internals.
// ---------------------------------------------------------------------------

/// <summary>
/// Petición de build de imagen Docker que el central envía al satélite.
/// <para>
/// El central prepara el build context completo (Dockerfile + sources) como un
/// tarball, lo comprime y lo serializa en Base64 dentro de <see cref="ContextTarballBase64"/>.
/// El satélite decodifica el tarball y lo pasa a <c>BuildImageFromDockerfileAsync</c>
/// del cliente Docker local.
/// </para>
/// </summary>
public sealed record BuildImageRequest(
    string BuildJobId,
    string ContextTarballBase64,
    string DockerfileRelativePath,
    string ImageTag,
    IReadOnlyDictionary<string, string> BuildArgs);

/// <summary>Resultado del build de imagen. <see cref="LogLines"/> contiene la salida del builder.</summary>
public sealed record BuildImageResult(
    string BuildJobId,
    bool Success,
    string? ImageId,
    string? ErrorMessage,
    IReadOnlyList<string> LogLines);

/// <summary>
/// Petición de creación + arranque de un contenedor a partir de una imagen ya disponible.
/// Si la imagen no existe localmente, el satélite hará <c>Images.CreateImageAsync</c> (pull) primero.
/// </summary>
public sealed record RunContainerRequest(
    string DeployJobId,
    string ImageRef,
    string ContainerName,
    IReadOnlyDictionary<string, string> EnvVars,
    IReadOnlyList<ContainerPortBinding> Ports,
    IReadOnlyList<ContainerVolumeMount> Volumes,
    string? NetworkName,
    ContainerHealthcheckSpec? Healthcheck);

/// <summary>Binding container-port ↔ host-port. Si <see cref="HostPort"/> es null Docker asigna uno random.</summary>
public sealed record ContainerPortBinding(int ContainerPort, int? HostPort, string Protocol);

/// <summary>Volumen montado en el contenedor. <see cref="Source"/> es host path o nombre de volume Docker.</summary>
public sealed record ContainerVolumeMount(string Source, string Target, bool ReadOnly);

/// <summary>Healthcheck del contenedor (equivalente al HEALTHCHECK del Dockerfile).</summary>
public sealed record ContainerHealthcheckSpec(string[] Cmd, TimeSpan Interval, TimeSpan Timeout, int Retries);

/// <summary>Resultado del create + start del contenedor.</summary>
public sealed record RunContainerResult(string DeployJobId, bool Success, string? ContainerId, string? ErrorMessage);

/// <summary>Stop graceful (SIGTERM + espera <see cref="Timeout"/>, luego SIGKILL).</summary>
public sealed record StopContainerRequest(string ContainerName, TimeSpan Timeout);

/// <summary>Eliminación del contenedor. <see cref="Force"/> implica <c>docker rm -f</c>.</summary>
public sealed record RemoveContainerRequest(string ContainerName, bool Force);

/// <summary>Stream de logs del contenedor. Si <see cref="Follow"/>=true, sigue emitiendo nuevas líneas.</summary>
public sealed record StreamLogsRequest(string ContainerName, int TailLines, bool Follow);

/// <summary>Trozo de log emitido por el satélite. <see cref="Stream"/> ∈ {"stdout","stderr"}.</summary>
public sealed record LogChunk(string ContainerName, string Stream, string Text);

/// <summary>Petición de listado de contenedores (equivale a <c>docker ps -a</c>).</summary>
public sealed record ListContainersRequest();

/// <summary>Resumen de un contenedor para el listado.</summary>
public sealed record ContainerSummary(string Id, string Name, string Image, string State, string Status);
