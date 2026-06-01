using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.Environments.Commands;

/// <summary>
/// Asigna <c>Order</c> en bloque siguiendo el array <paramref name="Ids"/>: el primer id
/// recibe order 0, el segundo order 1, etc. Es la operación que la UI llama cuando el
/// usuario arrastra (o usa botones up/down) — un solo SaveChanges atómico.
/// </summary>
public sealed record ReorderEnvironmentDefinitionsCommand(IReadOnlyList<string> Ids) : ICommand;

public sealed class ReorderEnvironmentDefinitionsValidator : AbstractValidator<ReorderEnvironmentDefinitionsCommand>
{
    public ReorderEnvironmentDefinitionsValidator()
    {
        RuleFor(c => c.Ids).NotEmpty();
    }
}

internal sealed class ReorderEnvironmentDefinitionsHandler(SettingsDbContext db)
    : ICommandHandler<ReorderEnvironmentDefinitionsCommand>
{
    public async Task<Result> Handle(ReorderEnvironmentDefinitionsCommand request, CancellationToken cancellationToken)
    {
        if (request.Ids.Count == 0)
        {
            return Error.Validation("settings.reorder_empty", "Debes enviar al menos un id.");
        }

        // Parse + dedup. Si vienen ids repetidos o mal formados, abortamos antes de tocar BD.
        var parsedIds = new List<EnvironmentDefinitionId>(request.Ids.Count);
        var seen = new HashSet<EnvironmentDefinitionId>();
        foreach (var raw in request.Ids)
        {
            var parsed = IdParsing.ParseEnvironmentDefinitionId(raw);
            if (parsed.IsFailure)
            {
                return parsed.Error;
            }
            if (!seen.Add(parsed.Value))
            {
                return Error.Validation("settings.reorder_duplicate", $"ID duplicado en la lista: '{raw}'.");
            }
            parsedIds.Add(parsed.Value);
        }

        var entities = await db.EnvironmentDefinitions
            .Where(e => parsedIds.Contains(e.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entities.Count != parsedIds.Count)
        {
            return Error.NotFound("settings.environment_not_found", "Al menos un ambiente del orden no existe.");
        }

        var byId = entities.ToDictionary(e => e.Id);
        for (var i = 0; i < parsedIds.Count; i++)
        {
            byId[parsedIds[i]].SetOrder(i);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
