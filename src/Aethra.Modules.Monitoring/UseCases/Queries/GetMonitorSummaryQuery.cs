using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Monitoring.UseCases.Queries;

/// <summary>
/// Conteos agregados de monitores por estado: una sola consulta a BD para el card del dashboard.
/// Los monitores deshabilitados se cuentan aparte (no influyen en los conteos de status).
/// </summary>
public sealed record GetMonitorSummaryQuery : IQuery<MonitorOverviewDto>;

internal sealed class GetMonitorSummaryHandler(MonitoringDbContext db)
    : IQueryHandler<GetMonitorSummaryQuery, MonitorOverviewDto>
{
    public async Task<Result<MonitorOverviewDto>> Handle(GetMonitorSummaryQuery request, CancellationToken ct)
    {
        var counts = await db.Monitors
            .AsNoTracking()
            .GroupBy(m => new { m.IsEnabled, m.LastStatus })
            .Select(g => new { g.Key.IsEnabled, g.Key.LastStatus, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        var total = counts.Sum(c => c.Count);
        var disabled = counts.Where(c => !c.IsEnabled).Sum(c => c.Count);
        var up = counts.Where(c => c.IsEnabled && c.LastStatus == MonitorStatus.Up).Sum(c => c.Count);
        var down = counts.Where(c => c.IsEnabled && c.LastStatus == MonitorStatus.Down).Sum(c => c.Count);
        var degraded = counts.Where(c => c.IsEnabled && c.LastStatus == MonitorStatus.Degraded).Sum(c => c.Count);
        var unknown = counts.Where(c => c.IsEnabled && c.LastStatus == MonitorStatus.Unknown).Sum(c => c.Count);

        return Result.Success(new MonitorOverviewDto(total, up, down, degraded, unknown, disabled));
    }
}
