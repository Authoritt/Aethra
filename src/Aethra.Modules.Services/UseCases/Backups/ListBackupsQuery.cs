using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Backups;

public sealed record ListBackupsQuery(string ServiceId, int Limit)
    : IQuery<IReadOnlyList<ServiceBackupDto>>;

internal sealed class ListBackupsHandler(ServicesDbContext db)
    : IQueryHandler<ListBackupsQuery, IReadOnlyList<ServiceBackupDto>>
{
    public async Task<Result<IReadOnlyList<ServiceBackupDto>>> Handle(ListBackupsQuery request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ServiceId, out var parsed) || parsed.Value.Prefix != "svc")
        {
            return Error.Validation("service.invalid_id", $"ServiceId invalido: '{request.ServiceId}'.");
        }
        var sid = new ManagedServiceId(parsed.Value);
        var limit = Math.Clamp(request.Limit, 1, 500);

        var rows = await db.ServiceBackups
            .AsNoTracking()
            .Where(b => b.ServiceId == sid)
            .OrderByDescending(b => b.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ServiceBackupDto> result = rows.Select(b => new ServiceBackupDto(
            b.Id.ToString(),
            b.ServiceId.ToString(),
            b.StartedAt,
            b.FinishedAt,
            b.Status.ToString(),
            b.SizeBytes,
            b.DestinationPath,
            b.ErrorMessage)).ToList();

        return Result<IReadOnlyList<ServiceBackupDto>>.Success(result);
    }
}
