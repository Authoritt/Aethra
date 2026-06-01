using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Deployments.UseCases.Build.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.UseCases.Build.Queries;

/// <summary>
/// Devuelve las líneas del log de un build a partir de <paramref name="Since"/> (exclusivo
/// hacia abajo del cliente). La UI hace polling incremental con la última sequence vista
/// para no recibir las líneas ya pintadas.
/// </summary>
public sealed record GetBuildLogsQuery(string BuildId, long Since = 0)
    : IQuery<IReadOnlyList<BuildLogChunkDto>>;

internal sealed class GetBuildLogsHandler(DeploymentsDbContext db)
    : IQueryHandler<GetBuildLogsQuery, IReadOnlyList<BuildLogChunkDto>>
{
    public async Task<Result<IReadOnlyList<BuildLogChunkDto>>> Handle(GetBuildLogsQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.BuildId, out var parsed) || parsed.Value.Prefix != "bld")
        {
            return Error.Validation("build.invalid_id", "ID de build inválido.");
        }
        var typed = new BuildId(parsed.Value);

        var logs = await db.BuildLogs
            .AsNoTracking()
            .Where(l => l.BuildId == typed && l.Sequence >= request.Since)
            .OrderBy(l => l.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = logs.Select(l => new BuildLogChunkDto(
            BuildId: l.BuildId.ToString(),
            Sequence: l.Sequence,
            Timestamp: l.Timestamp,
            Level: l.Level.ToString().ToLowerInvariant(),
            Stage: l.Stage,
            Text: l.Text)).ToList();

        return Result.Success<IReadOnlyList<BuildLogChunkDto>>(dtos);
    }
}
