using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Backups;

/// <summary>
/// Lee la política de backups AUTOMÁTICOS de un servicio (la contraparte read del SetBackupPolicy, que
/// hasta ahora era write-only). Devuelve null si no hay política configurada. Permite a la UI mostrar
/// el cron/retención/destino vigente (incluido satellite://).
/// </summary>
public sealed record GetBackupPolicyQuery(string ServiceId) : IQuery<BackupPolicyDto?>;

internal sealed class GetBackupPolicyHandler(ServicesDbContext db)
    : IQueryHandler<GetBackupPolicyQuery, BackupPolicyDto?>
{
    public async Task<Result<BackupPolicyDto?>> Handle(GetBackupPolicyQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.ServiceId, out var parsed) || parsed.Value.Prefix != "svc")
        {
            return Error.Validation("service.invalid_id", $"ServiceId invalido: '{request.ServiceId}'.");
        }
        var sid = new ManagedServiceId(parsed.Value);
        var svc = await db.ManagedServices.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sid, ct)
            .ConfigureAwait(false);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", $"Servicio '{request.ServiceId}' no existe.");
        }

        var p = svc.BackupPolicy;
        return p is null
            ? Result<BackupPolicyDto?>.Success(null)
            : Result<BackupPolicyDto?>.Success(new BackupPolicyDto(p.CronExpression, p.RetentionCount, p.Destination));
    }
}
