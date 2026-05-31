namespace Aethra.Modules.Deployments.Infrastructure.Git;

/// <summary>
/// Abstrae el clone de un repositorio Git a disco temporal.
/// El consumidor típico es <c>DeployWorker</c>, que necesita el código en disco
/// para luego pasarlo a <c>docker build</c> como build context.
/// </summary>
public interface IGitCloner
{
    /// <summary>
    /// Clona el repo en un directorio temporal único y devuelve un handle.
    /// El handle es <see cref="IAsyncDisposable"/> y borra el directorio al ser dispuesto.
    /// </summary>
    /// <param name="repoUrl">URL HTTPS del repo (HTTPS, no SSH; auth via token).</param>
    /// <param name="branch">Branch a checkout (ej. <c>main</c>).</param>
    /// <param name="accessToken">
    /// PAT/OAuth token para repos privados. Si es <c>null</c> el clone se hace sin credenciales
    /// (asume repo público).
    /// </param>
    /// <param name="sparseBaseDirectory">
    /// Subdirectorio dentro del repo (monorepo). Si está definido, el <see cref="ClonedRepo.BuildContext"/>
    /// apunta a ese subdirectorio, no a la raíz del clone.
    /// Aclaración: LibGit2Sharp NO soporta sparse-checkout real, por lo que el clone descarga
    /// el repo completo y solo limita el path expuesto como build context. Ver TODO en el cloner.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<ClonedRepo> CloneAsync(
        string repoUrl,
        string branch,
        string? accessToken,
        string? sparseBaseDirectory,
        CancellationToken ct);
}
