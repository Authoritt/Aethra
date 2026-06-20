using Aethra.Modules.Metrics.Infrastructure;
using Aethra.Shared.Contracts.Vms;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Metrics.UseCases.Queries;

/// <summary>
/// Devuelve el último snapshot de contenedores reportado por el satélite de una VM: TODOS los
/// contenedores del host (gestionados por Aethra o no) con sus stats de uso. Para el panel de
/// contenedores del detalle de VM (carga inicial; las actualizaciones en vivo llegan por SignalR).
/// </summary>
public sealed record GetVmContainersQuery(string VmId) : IQuery<VmContainersDto>;

public sealed record VmContainersDto(
    DateTimeOffset? Timestamp,
    IReadOnlyList<ContainerInfo> Containers);

internal sealed class GetVmContainersHandler(MetricsDbContext db)
    : IQueryHandler<GetVmContainersQuery, VmContainersDto>
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public async Task<Result<VmContainersDto>> Handle(GetVmContainersQuery request, CancellationToken ct)
    {
        var latest = await db.ContainerSnapshots
            .AsNoTracking()
            .Where(s => s.VmId == request.VmId)
            .OrderByDescending(s => s.Timestamp)
            .Select(s => new { s.Timestamp, s.ContainersJson })
            .FirstOrDefaultAsync(ct);

        if (latest is null)
        {
            return Result.Success(new VmContainersDto(null, []));
        }

        List<ContainerInfo> containers;
        try
        {
            containers = System.Text.Json.JsonSerializer
                .Deserialize<List<ContainerInfo>>(latest.ContainersJson, JsonOptions) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            containers = [];
        }

        // Orden estable y útil: corriendo primero, luego por nombre.
        containers.Sort((a, b) =>
        {
            var aRun = string.Equals(a.State, "running", StringComparison.OrdinalIgnoreCase);
            var bRun = string.Equals(b.State, "running", StringComparison.OrdinalIgnoreCase);
            if (aRun != bRun)
            {
                return aRun ? -1 : 1;
            }
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return Result.Success(new VmContainersDto(latest.Timestamp, containers));
    }
}
