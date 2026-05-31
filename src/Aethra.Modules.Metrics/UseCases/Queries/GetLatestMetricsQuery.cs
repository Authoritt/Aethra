using Aethra.Modules.Metrics.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Metrics.UseCases.Queries;

public sealed record GetLatestMetricsQuery(string VmId, int Limit = 60) : IQuery<IReadOnlyList<VmMetricPoint>>;

public sealed record VmMetricPoint(
    DateTimeOffset Timestamp,
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    long NetBytesReceived,
    long NetBytesSent);

internal sealed class GetLatestMetricsHandler(MetricsDbContext db)
    : IQueryHandler<GetLatestMetricsQuery, IReadOnlyList<VmMetricPoint>>
{
    public async Task<Result<IReadOnlyList<VmMetricPoint>>> Handle(GetLatestMetricsQuery request, CancellationToken ct)
    {
        var clampedLimit = Math.Clamp(request.Limit, 1, 1000);
        var points = await db.VmMetrics
            .AsNoTracking()
            .Where(m => m.VmId == request.VmId)
            .OrderByDescending(m => m.Timestamp)
            .Take(clampedLimit)
            .Select(m => new VmMetricPoint(
                m.Timestamp,
                m.CpuPercent,
                m.MemoryUsedBytes,
                m.MemoryTotalBytes,
                m.NetBytesReceived,
                m.NetBytesSent))
            .ToListAsync(ct);

        // Devolver en orden cronológico (más viejo primero) para graficar.
        points.Reverse();
        return Result.Success<IReadOnlyList<VmMetricPoint>>(points);
    }
}
