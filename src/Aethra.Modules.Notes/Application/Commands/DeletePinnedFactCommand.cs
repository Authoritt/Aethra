using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notes.Application.Commands;

public sealed record DeletePinnedFactCommand(string FactId) : ICommand;

internal sealed class DeletePinnedFactHandler(NotesDbContext db) : ICommandHandler<DeletePinnedFactCommand>
{
    public async Task<Result> Handle(DeletePinnedFactCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.FactId, out var parsed) || parsed.Value.Prefix != "pf")
        {
            return Error.Validation("pinned_fact.invalid_id", $"FactId inválido: '{request.FactId}'.");
        }
        var typedId = new PinnedFactId(parsed.Value);

        var fact = await db.PinnedFacts.FirstOrDefaultAsync(f => f.Id == typedId, cancellationToken)
            .ConfigureAwait(false);

        if (fact is null)
        {
            return Error.NotFound("pinned_fact.not_found", $"PinnedFact '{request.FactId}' no encontrado.");
        }

        db.PinnedFacts.Remove(fact);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
