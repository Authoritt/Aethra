namespace Aethra.Modules.Notes.Domain;

/// <summary>
/// Almacén binario de imágenes adjuntas a notas. Vive fuera de la BD para no inflar Postgres
/// con bytes (blobs). Implementación local: filesystem; en producción podría ir a S3/MinIO sin
/// tocar el dominio.
///
/// Diseño: el caller genera el <c>imageId</c> (Guid) y el store devuelve el <c>storedFilename</c>
/// — la ruta relativa que se persiste en la owned entity <see cref="NoteImage"/>.
/// </summary>
public interface INoteImageStore
{
    Task<string> SaveAsync(Guid imageId, string originalFilename, string contentType, Stream content, CancellationToken ct);

    Task<Stream?> OpenReadAsync(Guid imageId, CancellationToken ct);

    Task DeleteAsync(Guid imageId, CancellationToken ct);
}
