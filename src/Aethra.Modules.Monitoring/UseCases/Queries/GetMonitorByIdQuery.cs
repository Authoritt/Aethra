using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Modules.Monitoring.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Monitoring.UseCases.Queries;

public sealed record GetMonitorByIdQuery(string MonitorId) : IQuery<MonitorDetailDto>;

internal sealed class GetMonitorByIdHandler(MonitoringDbContext db)
    : IQueryHandler<GetMonitorByIdQuery, MonitorDetailDto>
{
    public async Task<Result<MonitorDetailDto>> Handle(GetMonitorByIdQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.MonitorId, out var parsed) || parsed.Value.Prefix != "mon")
        {
            return Error.Validation("monitor.invalid_id", "ID de monitor inválido.");
        }
        var typedId = new MonitorId(parsed.Value);
        var monitor = await db.Monitors.AsNoTracking().FirstOrDefaultAsync(m => m.Id == typedId, ct).ConfigureAwait(false);
        if (monitor is null)
        {
            return Error.NotFound("monitor.not_found", $"Monitor '{request.MonitorId}' no existe.");
        }
        return MonitorMapper.ToDetail(monitor);
    }
}
