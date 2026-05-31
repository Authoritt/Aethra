using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Git;

/// <summary>
/// Handle de un clone Git en disco temporal. Al disponerse, borra el directorio
/// con retries para tolerar locks transitorios de Windows (file locks tras LibGit2Sharp,
/// índices de virus scanner, etc.).
/// </summary>
public sealed class ClonedRepo : IAsyncDisposable
{
    private static readonly TimeSpan[] DeleteRetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    ];

    private readonly ILogger? _logger;
    private int _disposed;

    /// <summary>Raíz absoluta del clone (directorio temporal único).</summary>
    public string WorkspaceRoot { get; }

    /// <summary>
    /// Directorio que debe usarse como build context. Igual a <see cref="WorkspaceRoot"/>
    /// salvo cuando se pidió <c>sparseBaseDirectory</c>; en ese caso apunta a
    /// <c>WorkspaceRoot/sparseBaseDirectory</c>.
    /// </summary>
    public string BuildContext { get; }

    /// <summary>SHA-1 hexadecimal completo del commit HEAD tras el clone.</summary>
    public string CommitSha { get; }

    /// <summary>Forma corta (7 chars) del <see cref="CommitSha"/>.</summary>
    public string ShortSha => CommitSha.Length >= 7 ? CommitSha[..7] : CommitSha;

    internal ClonedRepo(string workspaceRoot, string buildContext, string commitSha, ILogger? logger = null)
    {
        WorkspaceRoot = workspaceRoot;
        BuildContext = buildContext;
        CommitSha = commitSha;
        _logger = logger;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await DeleteWithRetriesAsync(WorkspaceRoot, _logger).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Borra el directorio con backoff. Windows mantiene handles abiertos sobre los packfiles
    /// inmediatamente después del clone; un par de retries cortos suele bastar.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:No capturar tipos de excepción generales",
        Justification = "Cleanup de mejor-esfuerzo: cualquier IOException, UnauthorizedAccessException o " +
                        "Win32Exception transitoria debe disparar retry. La excepción final se loguea.")]
    private static async Task DeleteWithRetriesAsync(string path, ILogger? logger)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Exception? lastException = null;
        foreach (var delay in DeleteRetryDelays)
        {
            try
            {
                // Limpia el atributo ReadOnly que LibGit2Sharp pone en algunos archivos del .git
                // y que en Windows bloquea Directory.Delete.
                ResetReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }

        // Último intento sin captura: si vuelve a fallar, propaga.
        try
        {
            ResetReadOnlyAttributes(path);
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "No se pudo borrar el directorio de clone {Path} tras los retries.", path);
        }
        finally
        {
            if (lastException is not null && Directory.Exists(path))
            {
                logger?.LogDebug("Directorio {Path} aún existe tras cleanup; último error: {Error}",
                    path, lastException.Message);
            }
        }
    }

    /// <summary>
    /// Quita el flag ReadOnly de los archivos del clone. LibGit2Sharp lo aplica sobre algunos
    /// archivos internos de <c>.git/objects/pack</c> y en Windows eso impide <c>Directory.Delete</c>.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:No capturar tipos de excepción generales",
        Justification = "Reset de atributos es best-effort. Si un archivo concreto falla, " +
                        "continuamos: la lógica de retries cubre el caso de borrado posterior.")]
    private static void ResetReadOnlyAttributes(string root)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                }
            }
        }
        catch (Exception)
        {
            // Best-effort: si no se puede enumerar (directorio ya borrado, permisos), seguimos.
        }
    }
}
