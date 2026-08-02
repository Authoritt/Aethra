using Aethra.Modules.Metrics.Infrastructure;
using Aethra.Shared.Contracts.Vms;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

internal sealed class GetVmContainersHandler(
    MetricsDbContext db,
    ILogger<GetVmContainersHandler> logger)
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
        catch (System.Text.Json.JsonException ex)
        {
            // Devolver [] aqui hace que un snapshot corrupto sea INDISTINGUIBLE de una VM
            // sin contenedores: el panel dibuja una maquina ociosa cuando puede estar
            // corriendolo todo. No se puede arreglar la forma del DTO sin cambiar a sus
            // consumidores, pero el fallo deja de ser mudo.
            logger.LogWarning(ex,
                "Snapshot de contenedores ILEGIBLE para la VM {VmId} (timestamp {Ts}): se devuelve lista vacia, que el panel mostrara como 'sin contenedores'. Longitud del JSON: {Bytes} bytes.",
                request.VmId, latest.Timestamp, latest.ContainersJson?.Length ?? 0);
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
