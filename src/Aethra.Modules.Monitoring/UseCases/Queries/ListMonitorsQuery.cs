using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Monitoring.UseCases.Queries;

/// <summary>
/// Listado filtrable de monitores. Todos los filtros son opcionales y "and" entre sí.
/// El status se acepta como string (case-insensitive) para que la UI no tenga que conocer el enum.
/// </summary>
public sealed record ListMonitorsQuery(
    string? InstanceId,
    string? ProjectId,
    string? Status,
    bool? IsEnabled) : IQuery<IReadOnlyList<MonitorSummaryDto>>;

internal sealed class ListMonitorsHandler(MonitoringDbContext db)
    : IQueryHandler<ListMonitorsQuery, IReadOnlyList<MonitorSummaryDto>>
{
    public async Task<Result<IReadOnlyList<MonitorSummaryDto>>> Handle(ListMonitorsQuery request, CancellationToken ct)
    {
        MonitorStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(request.Status)
            && Enum.TryParse<MonitorStatus>(request.Status, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var query = db.Monitors.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.InstanceId))
        {
            var ins = request.InstanceId;
            query = query.Where(m => m.InstanceId == ins);
        }
        if (!string.IsNullOrWhiteSpace(request.ProjectId))
        {
            var prj = request.ProjectId;
            query = query.Where(m => m.ProjectId == prj);
        }
        if (statusFilter is { } s)
        {
            query = query.Where(m => m.LastStatus == s);
        }
        if (request.IsEnabled is { } enabled)
        {
            query = query.Where(m => m.IsEnabled == enabled);
        }

        var rows = await query.OrderBy(m => m.Name).ToListAsync(ct).ConfigureAwait(false);
        IReadOnlyList<MonitorSummaryDto> dtos = rows.Select(MonitorMapper.ToSummary).ToList();
        return Result.Success(dtos);
    }
}
