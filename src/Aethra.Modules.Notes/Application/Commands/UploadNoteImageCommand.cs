using Aethra.Modules.Notes.Application.Dtos;
using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notes.Application.Commands;

/// <summary>
/// Adjunta una imagen a una nota. El presenter (endpoint REST) ya valida tamaño y MIME
/// (allowlist) antes de despachar el comando — aquí confiamos en esos valores.
/// </summary>
public sealed record UploadNoteImageCommand(
    string NoteId,
    string Filename,
    string ContentType,
    int SizeBytes,
    Stream Content) : ICommand<NoteImageDto>;

internal sealed class UploadNoteImageHandler(NotesDbContext db, IClock clock, INoteImageStore store)
    : ICommandHandler<UploadNoteImageCommand, NoteImageDto>
{
    public async Task<Result<NoteImageDto>> Handle(UploadNoteImageCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.NoteId, out var parsed) || parsed.Value.Prefix != "note")
        {
            return Error.Validation("note.invalid_id", $"NoteId inválido: '{request.NoteId}'.");
        }
        var typedId = new NoteId(parsed.Value);

        var note = await db.Notes.Include(n => n.Images)
            .FirstOrDefaultAsync(n => n.Id == typedId, cancellationToken)
            .ConfigureAwait(false);

        if (note is null)
        {
            return Error.NotFound("note.not_found", $"Nota '{request.NoteId}' no encontrada.");
        }

        var imageId = Guid.CreateVersion7();
        string storedFilename;
        try
        {
            storedFilename = await store.SaveAsync(imageId, request.Filename, request.ContentType, request.Content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return Error.Failure("note.image_store_failed", $"No se pudo guardar la imagen: {ex.Message}");
        }

        NoteImage image;
        try
        {
            image = note.AttachImage(
                imageId,
                request.Filename,
                storedFilename,
                request.ContentType,
                request.SizeBytes,
                clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            // Compensación: si la entidad rechaza la metadata, borrar el blob ya escrito.
            await store.DeleteAsync(imageId, CancellationToken.None).ConfigureAwait(false);
            return Error.Validation("note.invalid_image", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoteMapper.ToImageDto(image);
    }
}
