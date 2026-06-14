using Aethra.Modules.Metrics.Infrastructure;
using Aethra.Shared.Contracts.Vms;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Metrics.UseCases.Queries;

/// <summary>
/// Historial de métricas de una VM en una ventana temporal (default 24h), DOWNSAMPLED a <see cref="MaxPoints"/>
/// puntos por promedio por bucket. A diferencia de <see cref="GetLatestMetricsQuery"/> (últimas N muestras crudas),
/// esto cubre rangos largos sin devolver miles de puntos. El disco se omite en el historial (cpu/mem/net).
/// </summary>
public sealed record GetMetricsHistoryQuery(string VmId, int Hours = 24, int MaxPoints = 240)
    : IQuery<IReadOnlyList<VmMetricPoint>>;

internal sealed class GetMetricsHistoryHandler(MetricsDbContext db, IClock clock)
    : IQueryHandler<GetMetricsHistoryQuery, IReadOnlyList<VmMetricPoint>>
{
    public async Task<Result<IReadOnlyList<VmMetricPoint>>> Handle(GetMetricsHistoryQuery request, CancellationToken ct)
    {
        var hours = Math.Clamp(request.Hours, 1, 168);          // hasta 7 días
        var maxPoints = Math.Clamp(request.MaxPoints, 10, 1000);
        var cutoff = clock.UtcNow - TimeSpan.FromHours(hours);

        // Sólo columnas numéricas (sin DisksJson) para no parsear miles de jsonb en ventanas largas.
        var raw = await db.VmMetrics
            .AsNoTracking()
            .Where(m => m.VmId == request.VmId && m.Timestamp >= cutoff)
            .OrderBy(m => m.Timestamp)
            .Select(m => new VmMetricPoint(
                m.Timestamp,
                m.CpuPercent,
                m.MemoryUsedBytes,
                m.MemoryTotalBytes,
                0,
                0,
                m.NetBytesReceived,
                m.NetBytesSent))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success(MetricsDownsampler.Downsample(raw, maxPoints));
    }
}

/// <summary>
/// Downsampling por buckets contiguos: promedia cada grupo de <c>ceil(n/maxPoints)</c> puntos. Puro
/// (sin BD) → testeable. Si hay &lt;= maxPoints puntos, devuelve la lista tal cual.
/// </summary>
public static class MetricsDownsampler
{
    public static IReadOnlyList<VmMetricPoint> Downsample(IReadOnlyList<VmMetricPoint> points, int maxPoints)
    {
        if (maxPoints < 1)
        {
            maxPoints = 1;
        }
        if (points.Count <= maxPoints)
        {
            return points;
        }

        var bucketSize = (int)Math.Ceiling(points.Count / (double)maxPoints);
        var result = new List<VmMetricPoint>((points.Count + bucketSize - 1) / bucketSize);
        for (var i = 0; i < points.Count; i += bucketSize)
        {
            var end = Math.Min(i + bucketSize, points.Count);
            var n = end - i;
            double cpu = 0;
            long memUsed = 0, memTotal = 0, diskUsed = 0, diskTotal = 0, rx = 0, tx = 0;
            for (var j = i; j < end; j++)
            {
                var p = points[j];
                cpu += p.CpuPercent;
                memUsed += p.MemoryUsedBytes;
                memTotal += p.MemoryTotalBytes;
                diskUsed += p.DiskUsedBytes;
                diskTotal += p.DiskTotalBytes;
                rx += p.NetBytesReceived;
                tx += p.NetBytesSent;
            }
            // Timestamp del último punto del bucket (fin del intervalo) para graficar.
            result.Add(new VmMetricPoint(
                points[end - 1].Timestamp,
                cpu / n,
                memUsed / n,
                memTotal / n,
                diskUsed / n,
                diskTotal / n,
                rx / n,
                tx / n));
        }
        return result;
    }
}
