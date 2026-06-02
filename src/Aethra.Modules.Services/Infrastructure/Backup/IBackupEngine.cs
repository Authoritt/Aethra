using Aethra.Modules.Services.Domain;

namespace Aethra.Modules.Services.Infrastructure.Backup;

/// <summary>
/// Abstraccion para ejecutar el dump de un servicio. Cada <see cref="ServiceType"/> tiene su
/// engine (Postgres = pg_dumpall, Redis = SAVE + copy dump.rdb, Rabbit = GET /api/definitions).
/// </summary>
public interface IBackupEngine
{
    ServiceType Type { get; }

    /// <summary>
    /// Ejecuta el dump y devuelve los bytes del backup serializado. La implementacion decide el
    /// formato (sql.gz, rdb, json) — el destino solo persiste los bytes.
    /// </summary>
    Task<byte[]> CreateBackupAsync(ManagedService service, CancellationToken ct);

    /// <summary>
    /// Restaura el backup invirtiendo el dump. La implementacion debe ser idempotente o documentar
    /// que destruye datos existentes (psql -d &lt; dump.sql sobreescribe).
    /// </summary>
    Task RestoreBackupAsync(ManagedService service, byte[] backupContent, CancellationToken ct);
}

/// <summary>
/// Abstraccion para escribir/leer el blob del backup. Implementaciones: volume (disco local)
/// y S3 (Minio-compatible).
/// </summary>
public interface IBackupStorage
{
    /// <summary>Match para el esquema de URL del destination (volume://, s3://).</summary>
    bool Supports(string destinationScheme);

    Task<string> WriteAsync(string destinationBase, string fileName, byte[] content, CancellationToken ct);

    Task<byte[]> ReadAsync(string fullPath, CancellationToken ct);

    Task DeleteAsync(string fullPath, CancellationToken ct);
}
