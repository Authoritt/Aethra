using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Satellite.Storage;

/// <summary>
/// Implementación filesystem del <see cref="ISatelliteFileStore"/>. Raíz: <c>SatelliteOptions.RemoteStorePath</c>
/// → si no, <c>{DataVolumePath}/aethra-store</c> → si no, <c>/var/lib/aethra/store</c> (Linux) o temp (Windows dev).
/// Sanea cada ruta relativa (rechaza vacíos, absolutos y <c>..</c>) y verifica que el path resuelto quede
/// DENTRO de la raíz, para que el central nunca pueda escribir fuera del directorio acotado.
/// </summary>
public sealed class FilesystemSatelliteFileStore : ISatelliteFileStore
{
    private readonly string _root;
    private readonly ILogger<FilesystemSatelliteFileStore> _logger;

    public FilesystemSatelliteFileStore(IOptions<SatelliteOptions> options, ILogger<FilesystemSatelliteFileStore> logger)
    {
        _logger = logger;
        var opts = options.Value;
        var root = opts.RemoteStorePath;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = !string.IsNullOrWhiteSpace(opts.DataVolumePath)
                ? Path.Combine(opts.DataVolumePath!, "aethra-store")
                : (OperatingSystem.IsWindows()
                    ? Path.Combine(Path.GetTempPath(), "aethra", "store")
                    : "/var/lib/aethra/store");
        }
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<(string StoredPath, long Size)> StoreAsync(string relativePath, byte[] content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var full = ResolveSafe(relativePath);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(full, content, ct).ConfigureAwait(false);
        _logger.LogInformation("SatelliteFileStore: escritos {Bytes} bytes en {Path}", content.Length, full);
        return (full, content.LongLength);
    }

    public async Task<byte[]> ReadAsync(string relativePath, CancellationToken ct)
    {
        var full = ResolveSafe(relativePath);
        return await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct)
    {
        var full = ResolveSafe(relativePath);
        if (File.Exists(full))
        {
            File.Delete(full);
            _logger.LogInformation("SatelliteFileStore: borrado {Path}", full);
        }
        return Task.CompletedTask;
    }

    /// <summary>Resuelve la ruta relativa dentro de la raíz, rechazando traversal y rutas absolutas.</summary>
    private string ResolveSafe(string relativePath)
    {
        var rel = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
        if (rel.Length == 0)
        {
            throw new ArgumentException("relativePath vacío.", nameof(relativePath));
        }
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p is "." or "..")
            {
                throw new ArgumentException($"relativePath inválido (traversal): {relativePath}", nameof(relativePath));
            }
        }
        var combined = Path.GetFullPath(Path.Combine(_root, Path.Combine(parts)));
        if (combined != _root &&
            !combined.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"relativePath escapa de la raíz: {relativePath}", nameof(relativePath));
        }
        return combined;
    }
}
