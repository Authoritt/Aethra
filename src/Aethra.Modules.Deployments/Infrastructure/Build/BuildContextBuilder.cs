using System.Formats.Tar;
using System.IO.Compression;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Build;

/// <summary>
/// Resultado de construir el contexto de build: el tar.gz que se envía al satélite + el SHA
/// realmente materializado (puede diferir del solicitado si el checkout cayó al HEAD del branch)
/// + líneas de log para anexar al build.
/// </summary>
public sealed record BuildContextResult(byte[] TarGz, string ResolvedSha, IReadOnlyList<string> Log);

/// <summary>
/// Construye el contexto de build real: clona el repo Git, materializa el commit y empaqueta el
/// árbol (subdirectorio <c>BaseDirectory</c> si aplica) en un tar.gz que el satélite alimenta a
/// <c>docker build</c>. Reemplaza el "clone simulado" de F9.3 (gap 1).
/// </summary>
public interface IBuildContextBuilder
{
    Task<BuildContextResult> BuildAsync(
        string gitRepoUrl,
        string branch,
        string? gitSha,
        string baseDirectory,
        CancellationToken ct);
}

/// <summary>
/// Implementación sobre LibGit2Sharp (clone full + checkout del SHA). Empaqueta con
/// <see cref="TarWriter"/> + GZip (built-in .NET). Excluye <c>.git</c> del contexto.
///
/// Nota: LibGit2Sharp no hace shallow clone; para repos grandes conviene migrar a
/// <c>git clone --depth</c> vía CLI. Para el caso normal (repos de apps) el clone full es
/// aceptable y evita depender de que <c>git</c> esté en el PATH del proceso de la API.
/// </summary>
public sealed class BuildContextBuilder(ILogger<BuildContextBuilder> logger) : IBuildContextBuilder
{
    public async Task<BuildContextResult> BuildAsync(
        string gitRepoUrl,
        string branch,
        string? gitSha,
        string baseDirectory,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitRepoUrl);
        var log = new List<string>();
        var workDir = Path.Combine(Path.GetTempPath(), "aethra-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            // === Clone ===
            var cloneOptions = new CloneOptions { Checkout = true };
            if (!string.IsNullOrWhiteSpace(branch))
            {
                cloneOptions.BranchName = branch;
            }
            log.Add($"Clonando {gitRepoUrl} (branch={branch})...");
            await Task.Run(() => Repository.Clone(gitRepoUrl, workDir, cloneOptions), ct)
                .ConfigureAwait(false);

            // === Checkout del SHA solicitado (si es real y existe en el historial) ===
            string resolvedSha;
            using (var repo = new Repository(workDir))
            {
                if (!string.IsNullOrWhiteSpace(gitSha)
                    && repo.Head.Tip is not null
                    && !string.Equals(repo.Head.Tip.Sha, gitSha, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Commands.Checkout(repo, gitSha);
                        log.Add($"Checkout commit {Short(gitSha)} OK.");
                    }
                    catch (LibGit2SharpException)
                    {
                        log.Add($"No se pudo hacer checkout de {Short(gitSha)} (no está en el historial "
                            + $"del clone); se usa HEAD del branch {branch}.");
                    }
                }
                resolvedSha = repo.Head.Tip?.Sha ?? gitSha ?? string.Empty;
            }

            // === Resolver raíz del contexto (BaseDirectory) ===
            var contextRoot = workDir;
            if (!string.IsNullOrWhiteSpace(baseDirectory) && baseDirectory.Trim() is { Length: > 0 } sub)
            {
                var normalized = sub.Replace('\\', '/').Trim('/');
                var candidate = Path.GetFullPath(Path.Combine(workDir, normalized));
                if (!candidate.StartsWith(Path.GetFullPath(workDir), StringComparison.Ordinal)
                    || !Directory.Exists(candidate))
                {
                    throw new InvalidOperationException(
                        $"BaseDirectory '{baseDirectory}' no existe en el repo o escapa del contexto.");
                }
                contextRoot = candidate;
            }

            // === Empaquetar tar.gz (excluyendo .git) ===
            var tarGz = await Task.Run(() => PackContext(contextRoot), ct).ConfigureAwait(false);
            log.Add($"Contexto empaquetado: {tarGz.Length / 1024} KiB.");

            return new BuildContextResult(tarGz, resolvedSha, log);
        }
        finally
        {
            TryDelete(workDir);
        }
    }

    private static byte[] PackContext(string contextRoot)
    {
        var rootFull = Path.GetFullPath(contextRoot);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(rootFull, file).Replace('\\', '/');
                // Excluir metadata de git y artefactos pesados habituales.
                if (rel.StartsWith(".git/", StringComparison.Ordinal) || rel == ".git")
                {
                    continue;
                }
                tar.WriteEntry(file, rel);
            }
        }
        return ms.ToArray();
    }

    private static string Short(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    private void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                // .git deja archivos read-only en Windows; normalizar atributos antes de borrar.
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                }
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo limpiar el contexto de build temporal {Dir}", dir);
        }
    }
}
