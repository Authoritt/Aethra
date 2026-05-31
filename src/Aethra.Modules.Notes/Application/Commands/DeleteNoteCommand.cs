using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notes.Application.Commands;

public sealed record DeleteNoteCommand(string NoteId) : ICommand;

internal sealed class DeleteNoteHandler(NotesDbContext db, INoteImageStore imageStore) : ICommandHandler<DeleteNoteCommand>
{
    public async Task<Result> Handle(DeleteNoteCommand request, CancellationToken cancellationToken)
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

        // Cleanup de blobs antes de borrar la fila — si la BD falla luego al eliminar igual los blobs
        // ya no se referenciarán y un cleanup posterior los podría recuperar/limpiar.
        foreach (var image in note.Images.ToList())
        {
            await imageStore.DeleteAsync(image.Id, cancellationToken).ConfigureAwait(false);
        }

        note.MarkDeleted();
        db.Notes.Remove(note);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
