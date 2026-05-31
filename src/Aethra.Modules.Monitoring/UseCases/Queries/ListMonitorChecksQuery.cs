using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Monitoring.UseCases.Queries;

/// <summary>
/// Devuelve los últimos N checks de un monitor en orden cronológico ascendente — listo para
/// graficar como sparkline. <c>Limit</c> se clampa a [1, 1000].
/// </summary>
public sealed record ListMonitorChecksQuery(string MonitorId, int Limit = 100)
    : IQuery<IReadOnlyList<MonitorCheckDto>>;

internal sealed class ListMonitorChecksHandler(MonitoringDbContext db)
    : IQueryHandler<ListMonitorChecksQuery, IReadOnlyList<MonitorCheckDto>>
{
    public async Task<Result<IReadOnlyList<MonitorCheckDto>>> Handle(ListMonitorChecksQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.MonitorId, out var parsed) || parsed.Value.Prefix != "mon")
        {
            return Error.Validation("monitor.invalid_id", "ID de monitor inválido.");
        }
        var typedId = new MonitorId(parsed.Value);
        var limit = Math.Clamp(request.Limit, 1, 1000);

        var rows = await db.MonitorChecks
            .AsNoTracking()
            .Where(c => c.MonitorId == typedId)
            .OrderByDescending(c => c.Timestamp)
            .Take(limit)
            .ToListAsync(ct).ConfigureAwait(false);

        // Devolvemos en orden cronológico ascendente para graficar.
        rows.Reverse();
        IReadOnlyList<MonitorCheckDto> dtos = rows.Select(MonitorMapper.ToCheckDto).ToList();
        return Result.Success(dtos);
    }
}
