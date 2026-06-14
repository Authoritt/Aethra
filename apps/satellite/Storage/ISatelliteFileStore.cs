namespace Aethra.Satellite.Storage;

/// <summary>
/// Almacén de blobs en el disco del satélite (p.ej. backups que el central descarga aquí para liberar
/// el disco del central). Todas las rutas son relativas a un directorio base acotado; la implementación
/// sanea contra path traversal antes de tocar el filesystem.
/// </summary>
public interface ISatelliteFileStore
{
    /// <summary>Escribe <paramref name="content"/> en <paramref name="relativePath"/>. Devuelve la ruta absoluta y el tamaño.</summary>
    Task<(string StoredPath, long Size)> StoreAsync(string relativePath, byte[] content, CancellationToken ct);

    /// <summary>Lee el blob en <paramref name="relativePath"/>.</summary>
    Task<byte[]> ReadAsync(string relativePath, CancellationToken ct);

    /// <summary>Borra el blob en <paramref name="relativePath"/> si existe (idempotente).</summary>
    Task DeleteAsync(string relativePath, CancellationToken ct);
}
