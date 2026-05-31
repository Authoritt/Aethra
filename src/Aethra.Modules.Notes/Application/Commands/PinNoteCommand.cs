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

public sealed record PinNoteCommand(string NoteId, bool Pinned) : ICommand<NoteDetailDto>;

internal sealed class PinNoteHandler(NotesDbContext db, IClock clock) : ICommandHandler<PinNoteCommand, NoteDetailDto>
{
    public async Task<Result<NoteDetailDto>> Handle(PinNoteCommand request, CancellationToken cancellationToken)
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

        var now = clock.UtcNow;
        if (request.Pinned)
        {
            note.Pin(now);
        }
        else
        {
            note.Unpin(now);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoteMapper.ToDetail(note);
    }
}
