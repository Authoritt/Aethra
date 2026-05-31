using Aethra.Modules.Notes.Application.Dtos;
using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;

namespace Aethra.Modules.Notes.Application.Commands;

public sealed record CreateNoteCommand(
    NoteScopeType ScopeType,
    string ScopeId,
    string Title,
    string MarkdownBody,
    string? AuthorId = null) : ICommand<NoteDetailDto>;

public sealed class CreateNoteValidator : AbstractValidator<CreateNoteCommand>
{
    public CreateNoteValidator()
    {
        RuleFor(c => c.ScopeId).NotEmpty().MaximumLength(64);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(255);
        RuleFor(c => c.MarkdownBody).NotNull();
    }
}

internal sealed class CreateNoteHandler(NotesDbContext db, IClock clock) : ICommandHandler<CreateNoteCommand, NoteDetailDto>
{
    public async Task<Result<NoteDetailDto>> Handle(CreateNoteCommand request, CancellationToken cancellationToken)
    {
        Note note;
        try
        {
            note = Note.Create(
                request.ScopeType,
                request.ScopeId,
                request.Title,
                request.MarkdownBody,
                clock.UtcNow,
                request.AuthorId);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("note.invalid", ex.Message);
        }

        db.Notes.Add(note);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoteMapper.ToDetail(note);
    }
}
