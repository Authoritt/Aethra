using Aethra.Shared.Contracts.Deployments;

namespace Aethra.Satellite.Docker;

/// <summary>
/// Abstracción del cliente Docker local del satélite.
/// <para>
/// El nombre del tipo intencionalmente no colisiona con <c>Docker.DotNet.IDockerClient</c>
/// porque vive en otro namespace (<see cref="Aethra.Satellite.Docker"/>) y representa
/// un contrato a más alto nivel: opera sobre los DTOs de Shared.Contracts, no sobre
/// los parameters específicos de Docker.DotNet. Aísla la lógica del worker de la
/// librería concreta y permite el fallback <c>DockerNotAvailableClient</c>.
/// </para>
/// </summary>
public interface IDockerClient
{
    /// <summary>Construye una imagen a partir de un tarball de contexto + Dockerfile.</summary>
    Task<BuildImageResult> BuildImageAsync(BuildImageRequest request, CancellationToken ct);

    /// <summary>Crea y arranca un contenedor a partir de una imagen.</summary>
    Task<RunContainerResult> RunContainerAsync(RunContainerRequest request, CancellationToken ct);

    /// <summary>Detiene gracefully un contenedor (SIGTERM → timeout → SIGKILL).</summary>
    Task StopContainerAsync(StopContainerRequest request, CancellationToken ct);

    /// <summary>Elimina un contenedor (force si fuese necesario).</summary>
    Task RemoveContainerAsync(RemoveContainerRequest request, CancellationToken ct);

    /// <summary>Streamea los logs del contenedor (stdout/stderr multiplexados).</summary>
    IAsyncEnumerable<LogChunk> StreamLogsAsync(StreamLogsRequest request, CancellationToken ct);

    /// <summary>Lista todos los contenedores conocidos por el daemon (incl. stopped).</summary>
    Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken ct);
}
