using Aethra.Modules.Notes.Application.Dtos;
using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notes.Application.Commands;

/// <summary>
/// Upsert por <c>(ScopeType, ScopeId, Key)</c>. Si existe → actualiza valor + flags; si no
/// → crea. El valor pasa por <see cref="IPinnedFactCodec"/> antes de tocar la BD.
/// </summary>
public sealed record UpsertPinnedFactCommand(
    NoteScopeType ScopeType,
    string ScopeId,
    string Key,
    string Value,
    bool IsSecret,
    string? Description) : ICommand<PinnedFactDto>;

public sealed class UpsertPinnedFactValidator : AbstractValidator<UpsertPinnedFactCommand>
{
    public UpsertPinnedFactValidator()
    {
        RuleFor(c => c.ScopeId).NotEmpty().MaximumLength(64);
        RuleFor(c => c.Key).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Value).NotNull();
        RuleFor(c => c.Description).MaximumLength(500);
    }
}

internal sealed class UpsertPinnedFactHandler(NotesDbContext db, IClock clock, IPinnedFactCodec codec)
    : ICommandHandler<UpsertPinnedFactCommand, PinnedFactDto>
{
    public async Task<Result<PinnedFactDto>> Handle(UpsertPinnedFactCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var cipher = codec.Encode(request.Value);

        var existing = await db.PinnedFacts.FirstOrDefaultAsync(
            f => f.ScopeType == request.ScopeType && f.ScopeId == request.ScopeId && f.Key == request.Key,
            cancellationToken).ConfigureAwait(false);

        PinnedFact fact;
        if (existing is not null)
        {
            existing.UpdateValue(cipher, request.IsSecret, request.Description, now);
            fact = existing;
        }
        else
        {
            try
            {
                fact = PinnedFact.Create(
                    request.ScopeType,
                    request.ScopeId,
                    request.Key,
                    cipher,
                    request.IsSecret,
                    request.Description,
                    now);
            }
            catch (ArgumentException ex)
            {
                return Error.Validation("pinned_fact.invalid", ex.Message);
            }
            db.PinnedFacts.Add(fact);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // El valor que devolvemos en este punto es el plaintext que vino en el request: no leakea
        // nada que el caller no haya enviado.
        return new PinnedFactDto(
            Id: fact.Id.ToString(),
            ScopeType: fact.ScopeType.ToString(),
            ScopeId: fact.ScopeId,
            Key: fact.Key,
            Value: request.Value,
            IsSecret: fact.IsSecret,
            Description: fact.Description,
            CreatedAt: fact.CreatedAt,
            UpdatedAt: fact.UpdatedAt);
    }
}
