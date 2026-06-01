using Aethra.Modules.Settings.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.BaseDomains.Commands;

public sealed record MarkWildcardConfiguredCommand(string BaseDomainId) : ICommand;

internal sealed class MarkWildcardConfiguredHandler(SettingsDbContext db, IClock clock)
    : ICommandHandler<MarkWildcardConfiguredCommand>
{
    public async Task<Result> Handle(MarkWildcardConfiguredCommand request, CancellationToken cancellationToken)
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

        domain.MarkWildcardConfigured(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
