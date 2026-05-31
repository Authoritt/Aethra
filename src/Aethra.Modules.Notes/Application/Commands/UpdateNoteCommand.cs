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

public sealed record UpdateNoteCommand(string NoteId, string? Title, string? MarkdownBody) : ICommand<NoteDetailDto>;

internal sealed class UpdateNoteHandler(NotesDbContext db, IClock clock) : ICommandHandler<UpdateNoteCommand, NoteDetailDto>
{
    public async Task<Result<NoteDetailDto>> Handle(UpdateNoteCommand request, CancellationToken cancellationToken)
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

        try
        {
            note.UpdateBody(request.Title, request.MarkdownBody, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("note.invalid", ex.Message);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoteMapper.ToDetail(note);
    }
}
