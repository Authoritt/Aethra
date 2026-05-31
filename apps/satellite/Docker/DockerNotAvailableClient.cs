using Aethra.Shared.Contracts.Deployments;
using Microsoft.Extensions.Logging;

namespace Aethra.Satellite.Docker;

/// <summary>
/// Fallback de <see cref="IDockerClient"/> cuando el socket Docker no está montado
/// (entornos de dev/test sin Docker instalado, o satélite corriendo en host sin daemon).
/// <para>
/// Todas las operaciones loguean una advertencia y retornan un resultado de fallo
/// estable, en vez de lanzar excepciones. Esto evita que el satélite quede en bucle
/// de reintentos y permite que el central marque jobs como fallidos con un mensaje claro.
/// </para>
/// </summary>
public sealed class DockerNotAvailableClient(ILogger<DockerNotAvailableClient> logger) : IDockerClient
{
    private const string Unavailable = "Docker no disponible en este satélite";

    public Task<BuildImageResult> BuildImageAsync(BuildImageRequest request, CancellationToken ct)
    {
        logger.LogWarning("BuildImage solicitado pero Docker no está disponible (job={Job})", request.BuildJobId);
        return Task.FromResult(new BuildImageResult(
            request.BuildJobId, Success: false, ImageId: null, ErrorMessage: Unavailable, LogLines: []));
    }

    public Task<RunContainerResult> RunContainerAsync(RunContainerRequest request, CancellationToken ct)
    {
        logger.LogWarning("RunContainer solicitado pero Docker no está disponible (job={Job})", request.DeployJobId);
        return Task.FromResult(new RunContainerResult(
            request.DeployJobId, Success: false, ContainerId: null, ErrorMessage: Unavailable));
    }

    public Task StopContainerAsync(StopContainerRequest request, CancellationToken ct)
    {
        logger.LogWarning("StopContainer solicitado pero Docker no está disponible (name={Name})", request.ContainerName);
        return Task.CompletedTask;
    }

    public Task RemoveContainerAsync(RemoveContainerRequest request, CancellationToken ct)
    {
        logger.LogWarning("RemoveContainer solicitado pero Docker no está disponible (name={Name})", request.ContainerName);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<LogChunk> StreamLogsAsync(
        StreamLogsRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        logger.LogWarning("StreamLogs solicitado pero Docker no está disponible (name={Name})", request.ContainerName);
        yield return new LogChunk(request.ContainerName, "stderr", Unavailable);
        await Task.CompletedTask;
    }

    public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken ct)
    {
        logger.LogWarning("ListContainers solicitado pero Docker no está disponible");
        return Task.FromResult<IReadOnlyList<ContainerSummary>>([]);
    }
}
