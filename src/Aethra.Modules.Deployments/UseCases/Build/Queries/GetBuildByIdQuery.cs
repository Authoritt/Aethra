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
/// Devuelve el detalle de un build por su ID. No incluye los logs — esos se piden
/// aparte vía <see cref="GetBuildLogsQuery"/> para que la UI los pagine.
/// </summary>
public sealed record GetBuildByIdQuery(string BuildId) : IQuery<BuildSummaryDto>;

internal sealed class GetBuildByIdHandler(DeploymentsDbContext db)
    : IQueryHandler<GetBuildByIdQuery, BuildSummaryDto>
{
    public async Task<Result<BuildSummaryDto>> Handle(GetBuildByIdQuery request, CancellationToken ct)
    {
        if (!AethraId.TryParse(request.BuildId, out var parsed) || parsed.Value.Prefix != "bld")
        {
            return Error.Validation("build.invalid_id", "ID de build inválido.");
        }
        var typed = new BuildId(parsed.Value);

        var build = await db.Builds.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == typed, ct)
            .ConfigureAwait(false);
        if (build is null)
        {
            return Error.NotFound("build.not_found", $"Build '{request.BuildId}' no existe.");
        }

        return BuildDtoMapper.ToSummary(build);
    }
}
