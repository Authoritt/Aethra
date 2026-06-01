using Aethra.Modules.Settings.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.IntegrationCredentials.Commands;

public sealed record DeleteIntegrationCredentialCommand(string CredentialId) : ICommand;

internal sealed class DeleteIntegrationCredentialHandler(SettingsDbContext db)
    : ICommandHandler<DeleteIntegrationCredentialCommand>
{
    public async Task<Result> Handle(DeleteIntegrationCredentialCommand request, CancellationToken cancellationToken)
    {
        var parsed = IdParsing.ParseCredentialId(request.CredentialId);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var credential = await db.IntegrationCredentials
            .FirstOrDefaultAsync(c => c.Id == parsed.Value, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return Error.NotFound("settings.credential_not_found", $"Credencial '{request.CredentialId}' no existe.");
        }

        db.IntegrationCredentials.Remove(credential);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
