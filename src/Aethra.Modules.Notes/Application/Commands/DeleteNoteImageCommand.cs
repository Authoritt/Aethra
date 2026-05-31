using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notes.Application.Commands;

public sealed record DeleteNoteImageCommand(string NoteId, Guid ImageId) : ICommand;

internal sealed class DeleteNoteImageHandler(NotesDbContext db, IClock clock, INoteImageStore store)
    : ICommandHandler<DeleteNoteImageCommand>
{
    public async Task<Result> Handle(DeleteNoteImageCommand request, CancellationToken cancellationToken)
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

        if (!note.DetachImage(request.ImageId, clock.UtcNow))
        {
            return Error.NotFound("note.image_not_found", $"Imagen '{request.ImageId}' no existe en la nota.");
        }

        await store.DeleteAsync(request.ImageId, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
