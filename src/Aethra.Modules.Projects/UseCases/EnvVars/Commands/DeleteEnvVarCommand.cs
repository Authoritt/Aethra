using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.EnvVars.Commands;

/// <summary>
/// Borra una env var por <paramref name="Key"/> dentro del scope polimórfico indicado, sin
/// importar su <c>Source</c> (borrado explícito por el dueño del scope). NotFound si no existe.
/// </summary>
public sealed record DeleteEnvVarCommand(string ScopeType, string ScopeId, string Key) : ICommand;

internal sealed class DeleteEnvVarHandler(ProjectsDbContext db)
    : ICommandHandler<DeleteEnvVarCommand>
{
    public async Task<Result> Handle(DeleteEnvVarCommand request, CancellationToken cancellationToken)
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
            return Error.Validation("env_var.invalid_key", "key no puede estar vacío.");
        }

        var rows = await db.EnvironmentVariables
            .Where(e => e.ScopeType == scopeType && e.ScopeId == scopeId && e.Key == key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return Error.NotFound("env_var.not_found",
                $"No existe la env var '{key}' en el scope indicado.");
        }

        db.EnvironmentVariables.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
