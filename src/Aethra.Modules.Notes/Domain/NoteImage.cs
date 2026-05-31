using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Notes.Domain;

/// <summary>
/// Imagen adjunta a una <see cref="Note"/>. Owned entity (vive solo dentro del agregado Note).
///
/// El binario NO se almacena en BD: <see cref="INoteImageStore"/> (infraestructura) lo escribe
/// en disco/object-store y aquí solo guardamos metadata + <see cref="StoredFilename"/> (la ruta
/// relativa, opaca para el dominio).
/// </summary>
public sealed class NoteImage : Entity<Guid>
{
    public string OriginalFilename { get; private set; }
    public string StoredFilename { get; private set; }
    public string ContentType { get; private set; }
    public int SizeBytes { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private NoteImage(
        Guid id,
        string originalFilename,
        string storedFilename,
        string contentType,
        int sizeBytes,
        DateTimeOffset uploadedAt) : base(id)
    {
        OriginalFilename = originalFilename;
        StoredFilename = storedFilename;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedAt = uploadedAt;
    }

    public static NoteImage Create(
        Guid id,
        string originalFilename,
        string storedFilename,
        string contentType,
        int sizeBytes,
        DateTimeOffset uploadedAt)
    {
        if (string.IsNullOrWhiteSpace(originalFilename))
        {
            throw new ArgumentException("El nombre original es obligatorio.", nameof(originalFilename));
        }
        if (string.IsNullOrWhiteSpace(storedFilename))
        {
            throw new ArgumentException("El nombre almacenado es obligatorio.", nameof(storedFilename));
        }
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("El content-type es obligatorio.", nameof(contentType));
        }
        if (sizeBytes <= 0)
        {
            throw new ArgumentException("El tamaño debe ser positivo.", nameof(sizeBytes));
        }
        return new NoteImage(id, originalFilename.Trim(), storedFilename.Trim(), contentType.Trim(), sizeBytes, uploadedAt);
    }

    // EF Core
    private NoteImage() : base()
    {
        OriginalFilename = string.Empty;
        StoredFilename = string.Empty;
        ContentType = string.Empty;
    }
}
