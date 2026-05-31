using Aethra.Modules.Notes.Application.Dtos;
using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notes.Application.Queries;

/// <summary>
/// Lista los pinned facts del scope. Si <c>IncludeSecretValues=false</c> los marcados como secret
/// se devuelven con valor mascarado <c>"********"</c>; los no-secret siempre devuelven plaintext.
/// Si <c>IncludeSecretValues=true</c>, todos vienen descifrados.
/// </summary>
public sealed record ListPinnedFactsQuery(
    NoteScopeType ScopeType,
    string ScopeId,
    bool IncludeSecretValues) : IQuery<IReadOnlyList<PinnedFactDto>>;

internal sealed class ListPinnedFactsHandler(NotesDbContext db, IPinnedFactCodec codec)
    : IQueryHandler<ListPinnedFactsQuery, IReadOnlyList<PinnedFactDto>>
{
    private const string Mask = "********";

    public async Task<Result<IReadOnlyList<PinnedFactDto>>> Handle(ListPinnedFactsQuery request, CancellationToken cancellationToken)
    {
        var facts = await db.PinnedFacts.AsNoTracking()
            .Where(f => f.ScopeType == request.ScopeType && f.ScopeId == request.ScopeId)
            .OrderBy(f => f.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dtos = new List<PinnedFactDto>(facts.Count);
        foreach (var fact in facts)
        {
            string value;
            if (fact.IsSecret && !request.IncludeSecretValues)
            {
                value = Mask;
            }
            else
            {
                try
                {
                    value = codec.Decode(fact.ValueCipher);
                }
                catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException || ex is System.Security.Cryptography.CryptographicException)
                {
                    // Si las llaves DataProtection se rotaron sin migrar, no romper el listado completo.
                    value = "<unreadable>";
                }
            }

            dtos.Add(new PinnedFactDto(
                Id: fact.Id.ToString(),
                ScopeType: fact.ScopeType.ToString(),
                ScopeId: fact.ScopeId,
                Key: fact.Key,
                Value: value,
                IsSecret: fact.IsSecret,
                Description: fact.Description,
                CreatedAt: fact.CreatedAt,
                UpdatedAt: fact.UpdatedAt));
        }

        IReadOnlyList<PinnedFactDto> result = dtos;
        return Result.Success(result);
    }
}
