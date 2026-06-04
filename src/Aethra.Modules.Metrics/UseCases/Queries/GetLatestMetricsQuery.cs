using Aethra.Modules.Metrics.Infrastructure;
using Aethra.Shared.Contracts.Vms;
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
    long DiskUsedBytes,
    long DiskTotalBytes,
    long NetBytesReceived,
    long NetBytesSent);

internal sealed class GetLatestMetricsHandler(MetricsDbContext db)
    : IQueryHandler<GetLatestMetricsQuery, IReadOnlyList<VmMetricPoint>>
{
    public async Task<Result<IReadOnlyList<VmMetricPoint>>> Handle(GetLatestMetricsQuery request, CancellationToken ct)
    {
        var clampedLimit = Math.Clamp(request.Limit, 1, 1000);
        // El disco vive serializado en DisksJson (jsonb); no se puede agregar en SQL, así que
        // traemos las columnas crudas y agregamos en memoria.
        var raw = await db.VmMetrics
            .AsNoTracking()
            .Where(m => m.VmId == request.VmId)
            .OrderByDescending(m => m.Timestamp)
            .Take(clampedLimit)
            .Select(m => new
            {
                m.Timestamp,
                m.CpuPercent,
                m.MemoryUsedBytes,
                m.MemoryTotalBytes,
                m.NetBytesReceived,
                m.NetBytesSent,
                m.DisksJson,
            })
            .ToListAsync(ct);

        var points = raw.Select(m =>
        {
            var (diskUsed, diskTotal) = MetricsDiskAggregator.Aggregate(m.DisksJson);
            return new VmMetricPoint(
                m.Timestamp,
                m.CpuPercent,
                m.MemoryUsedBytes,
                m.MemoryTotalBytes,
                diskUsed,
                diskTotal,
                m.NetBytesReceived,
                m.NetBytesSent);
        }).ToList();

        // Orden cronológico (más viejo primero) para graficar.
        points.Reverse();
        return Result.Success<IReadOnlyList<VmMetricPoint>>(points);
    }
}

/// <summary>
/// Agrega el uso de disco sumando todos los volúmenes fijos del snapshot. Compartido entre el
/// query REST (parsea <c>DisksJson</c>) y el forwarder SignalR (que ya tiene la lista tipada).
/// </summary>
public static class MetricsDiskAggregator
{
    public static (long Used, long Total) Aggregate(string? disksJson)
    {
        if (string.IsNullOrWhiteSpace(disksJson))
        {
            return (0, 0);
        }
        try
        {
            var disks = System.Text.Json.JsonSerializer.Deserialize<List<DiskUsage>>(disksJson);
            return Aggregate(disks);
        }
        catch (System.Text.Json.JsonException)
        {
            return (0, 0);
        }
    }

    public static (long Used, long Total) Aggregate(IReadOnlyList<DiskUsage>? disks)
    {
        if (disks is null || disks.Count == 0)
        {
            return (0, 0);
        }
        long used = 0, total = 0;
        foreach (var d in disks)
        {
            used += d.UsedBytes;
            total += d.TotalBytes;
        }
        return (used, total);
    }
}
