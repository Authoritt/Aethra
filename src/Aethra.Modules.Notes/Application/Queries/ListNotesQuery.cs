using Aethra.Modules.Notes.Application.Dtos;
using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Shared.Contracts.Notes;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notes.Application.Queries;

public sealed record ListNotesQuery(NoteScopeType ScopeType, string ScopeId) : IQuery<IReadOnlyList<NoteSummaryDto>>;

internal sealed class ListNotesHandler(NotesDbContext db) : IQueryHandler<ListNotesQuery, IReadOnlyList<NoteSummaryDto>>
{
    public async Task<Result<IReadOnlyList<NoteSummaryDto>>> Handle(ListNotesQuery request, CancellationToken cancellationToken)
    {
        var notes = await db.Notes
            .Include(n => n.Images)
            .AsNoTracking()
            .Where(n => n.ScopeType == request.ScopeType && n.ScopeId == request.ScopeId)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.UpdatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<NoteSummaryDto> result = [.. notes.Select(NoteMapper.ToSummary)];
        return Result.Success(result);
    }
}
