using Aethra.Modules.Settings.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.BaseDomains.Commands;

public sealed record DeleteBaseDomainCommand(string BaseDomainId) : ICommand;

internal sealed class DeleteBaseDomainHandler(SettingsDbContext db) : ICommandHandler<DeleteBaseDomainCommand>
{
    public async Task<Result> Handle(DeleteBaseDomainCommand request, CancellationToken cancellationToken)
    {
        var parsed = IdParsing.ParseBaseDomainId(request.BaseDomainId);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var domain = await db.BaseDomains
            .FirstOrDefaultAsync(d => d.Id == parsed.Value, cancellationToken)
            .ConfigureAwait(false);
        if (domain is null)
        {
            return Error.NotFound("settings.base_domain_not_found", $"Base domain '{request.BaseDomainId}' no existe.");
        }

        if (domain.IsActive)
        {
            return Error.Conflict(
                "settings.base_domain_active",
                "No se puede borrar el base domain activo. Activa otro primero o desactívalo.");
        }

        db.BaseDomains.Remove(domain);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
