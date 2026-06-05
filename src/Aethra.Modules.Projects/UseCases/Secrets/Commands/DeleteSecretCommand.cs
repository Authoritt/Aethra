using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.EnvVars;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Secrets.Commands;

/// <summary>
/// Borra un secreto por <paramref name="Key"/> dentro del scope polimórfico indicado, sin
/// importar su <c>Source</c> (borrado explícito por el dueño del scope). NotFound si no existe.
/// </summary>
public sealed record DeleteSecretCommand(string ScopeType, string ScopeId, string Key) : ICommand;

internal sealed class DeleteSecretHandler(ProjectsDbContext db)
    : ICommandHandler<DeleteSecretCommand>
{
    public async Task<Result> Handle(DeleteSecretCommand request, CancellationToken cancellationToken)
    {
        var scopeResult = ScopeParsing.ParseScopeType(request.ScopeType);
        if (scopeResult.IsFailure)
        {
            return scopeResult.Error;
        }
        var scopeType = scopeResult.Value;
        var scopeId = request.ScopeId ?? string.Empty;
        var key = (request.Key ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return Error.Validation("secret.invalid_key", "key no puede estar vacío.");
        }

        var rows = await db.Secrets
            .Where(s => s.ScopeType == scopeType && s.ScopeId == scopeId && s.Key == key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return Error.NotFound("secret.not_found",
                $"No existe el secreto '{key}' en el scope indicado.");
        }

        db.Secrets.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
