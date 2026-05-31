using System.Diagnostics.CodeAnalysis;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Git;

/// <summary>
/// Implementación de <see cref="IGitCloner"/> basada en LibGit2Sharp.
///
/// Limitaciones conocidas del binding nativo (libgit2) que aceptamos en F4:
/// <list type="bullet">
///   <item>
///     <b>No soporta <c>--depth=1</c>:</b> libgit2 no expone shallow clone. Descargamos siempre
///     todo el historial. Para repos grandes esto puede ser costoso; un fallback con el binario
///     <c>git</c> CLI queda como TODO para F5+.
///   </item>
///   <item>
///     <b>No soporta <c>sparse-checkout</c>:</b> tampoco está expuesto en el binding. Implementamos
///     "sparse lógico": el clone trae todo, pero <c>BuildContext</c> apunta al subdirectorio
///     pedido. El consumidor (DeployWorker) usa <c>BuildContext</c> como build context de Docker
///     y por tanto solo ese subdirectorio entra en la imagen.
///     TODO: para sparse real ejecutar <c>git sparse-checkout</c> via <c>Process</c> en F5+.
///   </item>
/// </list>
///
/// El servicio es stateless y se registra como singleton.
/// </summary>
public sealed class GitCloner : IGitCloner
{
    private const string CloneRootName = "aethra-clones";

    private readonly ILogger<GitCloner> _logger;

    public GitCloner(ILogger<GitCloner> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<ClonedRepo> CloneAsync(
        string repoUrl,
        string branch,
        string? accessToken,
        string? sparseBaseDirectory,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        // LibGit2Sharp es síncrono; envolvemos en Task.Run para no bloquear el hilo de la cola del worker.
        return Task.Run(() => CloneCore(repoUrl, branch, accessToken, sparseBaseDirectory, ct), ct);
    }

    [SuppressMessage(
        "Design",
        "CA1031:No capturar tipos de excepción generales",
        Justification = "Si LibGit2Sharp falla a mitad del clone debemos limpiar el directorio temporal " +
                        "sin filtrar el tipo de excepción (LibGit2SharpException, IOException, etc.) y " +
                        "re-lanzar para que el caller decida.")]
    private ClonedRepo CloneCore(
        string repoUrl,
        string branch,
        string? accessToken,
        string? sparseBaseDirectory,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var workspaceRoot = CreateTempCloneDirectory();
        _logger.LogDebug("Clonando {RepoUrl} (branch {Branch}) en {WorkspaceRoot}",
            repoUrl, branch, workspaceRoot);

        var options = BuildCloneOptions(branch, accessToken, ct);

        try
        {
            Repository.Clone(repoUrl, workspaceRoot, options);

            string commitSha;
            using (var repo = new Repository(workspaceRoot))
            {
                commitSha = repo.Head.Tip?.Sha
                    ?? throw new InvalidOperationException(
                        $"El clone de '{repoUrl}' no produjo un HEAD válido (¿branch vacía?).");
            }

            var buildContext = ResolveBuildContext(workspaceRoot, sparseBaseDirectory);

            _logger.LogInformation(
                "Clone OK: {RepoUrl}@{Branch} → {ShortSha} en {WorkspaceRoot} (build context: {BuildContext})",
                repoUrl, branch, commitSha[..7], workspaceRoot, buildContext);

            return new ClonedRepo(workspaceRoot, buildContext, commitSha, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el clone de {RepoUrl} (branch {Branch}). Limpiando {WorkspaceRoot}.",
                repoUrl, branch, workspaceRoot);
            TryCleanup(workspaceRoot);
            throw;
        }
    }

    private static string CreateTempCloneDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), CloneRootName, Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private CloneOptions BuildCloneOptions(string branch, string? accessToken, CancellationToken ct)
    {
        var options = new CloneOptions
        {
            IsBare = false,
            Checkout = true,
            BranchName = branch,
        };

        // En LibGit2Sharp 0.30+ los handlers de fetch viven en CloneOptions.FetchOptions
        // (heredados de FetchOptionsBase): CredentialsProvider, OnProgress, OnTransferProgress.
        var fetch = options.FetchOptions;
        fetch.CredentialsProvider = BuildCredentialsProvider(accessToken);

        // Cancelación cooperativa: LibGit2Sharp llama OnProgress/OnTransferProgress periódicamente.
        // Devolviendo false en OnTransferProgress aborta el clone limpiamente.
        fetch.OnProgress = serverProgressOutput =>
        {
            if (ct.IsCancellationRequested)
            {
                return false;
            }
            if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Trace) && !string.IsNullOrWhiteSpace(serverProgressOutput))
            {
                _logger.LogTrace("git remote: {Line}", serverProgressOutput.TrimEnd());
            }
            return true;
        };
        fetch.OnTransferProgress = _ => !ct.IsCancellationRequested;

        return options;
    }

    private static CredentialsHandler? BuildCredentialsProvider(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        // GitHub/GitLab/Gitea/Bitbucket aceptan el patrón "x-access-token" como user
        // y el PAT/OAuth token como password sobre HTTPS.
        return (_, _, _) => new UsernamePasswordCredentials
        {
            Username = "x-access-token",
            Password = accessToken,
        };
    }

    private static string ResolveBuildContext(string workspaceRoot, string? sparseBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(sparseBaseDirectory))
        {
            return workspaceRoot;
        }

        // Normaliza separadores y prohíbe traversal (..) para que el path no escape del clone.
        var trimmed = sparseBaseDirectory.Trim().Replace('\\', '/').Trim('/');
        if (trimmed.Length == 0)
        {
            return workspaceRoot;
        }
        if (trimmed.Split('/').Any(seg => seg == ".."))
        {
            throw new ArgumentException(
                $"sparseBaseDirectory '{sparseBaseDirectory}' contiene segmentos '..' (path traversal).",
                nameof(sparseBaseDirectory));
        }

        var combined = Path.GetFullPath(Path.Combine(workspaceRoot, trimmed));
        var rootFull = Path.GetFullPath(workspaceRoot);
        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"sparseBaseDirectory '{sparseBaseDirectory}' resuelve fuera del workspace.",
                nameof(sparseBaseDirectory));
        }

        if (!Directory.Exists(combined))
        {
            throw new DirectoryNotFoundException(
                $"El subdirectorio '{sparseBaseDirectory}' no existe en el repo clonado.");
        }
        return combined;
    }

    [SuppressMessage(
        "Design",
        "CA1031:No capturar tipos de excepción generales",
        Justification = "Cleanup post-fallo de clone: tragamos cualquier excepción para no enmascarar " +
                        "la excepción original del clone con un IOException de borrado.")]
    private void TryCleanup(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cleanup tras fallo de clone no pudo borrar {Path}", path);
        }
    }
}
