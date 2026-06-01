using Aethra.Modules.Settings.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.Environments.Commands;

public sealed record DeleteEnvironmentDefinitionCommand(string EnvironmentId) : ICommand;

internal sealed class DeleteEnvironmentDefinitionHandler(SettingsDbContext db)
    : ICommandHandler<DeleteEnvironmentDefinitionCommand>
{
    public async Task<Result> Handle(DeleteEnvironmentDefinitionCommand request, CancellationToken cancellationToken)
    {
        var parsed = IdParsing.ParseEnvironmentDefinitionId(request.EnvironmentId);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var env = await db.EnvironmentDefinitions
            .FirstOrDefaultAsync(e => e.Id == parsed.Value, cancellationToken)
            .ConfigureAwait(false);
        if (env is null)
        {
            return Error.NotFound("settings.environment_not_found", $"Ambiente '{request.EnvironmentId}' no existe.");
        }

        db.EnvironmentDefinitions.Remove(env);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
