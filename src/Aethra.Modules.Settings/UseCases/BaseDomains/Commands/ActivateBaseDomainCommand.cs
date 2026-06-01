using Aethra.Modules.Settings.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.BaseDomains.Commands;

/// <summary>
/// Activa el base domain indicado y desactiva todos los demás. La invariante "solo uno
/// activo" se aplica aquí (no en el aggregate) porque cruza múltiples instancias.
/// </summary>
public sealed record ActivateBaseDomainCommand(string BaseDomainId) : ICommand;

internal sealed class ActivateBaseDomainHandler(SettingsDbContext db, IClock clock)
    : ICommandHandler<ActivateBaseDomainCommand>
{
    public async Task<Result> Handle(ActivateBaseDomainCommand request, CancellationToken cancellationToken)
    {
        var parsed = IdParsing.ParseBaseDomainId(request.BaseDomainId);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var all = await db.BaseDomains.ToListAsync(cancellationToken).ConfigureAwait(false);
        var target = all.FirstOrDefault(d => d.Id == parsed.Value);
        if (target is null)
        {
            return Error.NotFound("settings.base_domain_not_found", $"Base domain '{request.BaseDomainId}' no existe.");
        }

        var now = clock.UtcNow;
        foreach (var domain in all)
        {
            if (domain.Id == target.Id)
            {
                domain.Activate(now);
            }
            else if (domain.IsActive)
            {
                domain.Deactivate(now);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
