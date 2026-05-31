using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.Zones.Commands;

/// <summary>
/// Quita la zona del registro local. Aborta si quedan DNS records gestionados por Aethra,
/// para forzar al operador a limpiarlos primero (evitamos huerfanos en Cloudflare).
/// </summary>
public sealed record DeleteZoneCommand(string ZoneId) : ICommand;

internal sealed class DeleteZoneHandler(CloudflareDbContext db, IClock clock) : ICommandHandler<DeleteZoneCommand>
{
    public async Task<Result> Handle(DeleteZoneCommand request, CancellationToken cancellationToken)
    {
        _ = clock;
        var idResult = IdParsing.ParseZoneId(request.ZoneId);
        if (idResult.IsFailure)
        {
            return idResult.Error;
        }
        var zoneId = idResult.Value;

        var zone = await db.Zones.FirstOrDefaultAsync(z => z.Id == zoneId, cancellationToken).ConfigureAwait(false);
        if (zone is null)
        {
            return Error.NotFound("cloudflare.zone_not_found", $"Zona '{request.ZoneId}' no existe.");
        }

        var hasRecords = await db.DnsRecords.AnyAsync(r => r.ZoneId == zoneId, cancellationToken).ConfigureAwait(false);
        if (hasRecords)
        {
            return Error.Conflict(
                "cloudflare.zone_has_records",
                "La zona tiene DNS records gestionados. Eliminarlos antes de quitar la zona.");
        }

        db.Zones.Remove(zone);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
