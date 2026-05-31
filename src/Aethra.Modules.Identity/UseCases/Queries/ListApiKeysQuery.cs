using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.UseCases.Queries;

public sealed record ListApiKeysQuery() : IQuery<IReadOnlyList<ApiKeySummaryDto>>;

internal sealed class ListApiKeysHandler(IdentityDbContext db)
    : IQueryHandler<ListApiKeysQuery, IReadOnlyList<ApiKeySummaryDto>>
{
    public async Task<Result<IReadOnlyList<ApiKeySummaryDto>>> Handle(ListApiKeysQuery request, CancellationToken cancellationToken)
    {
        var keys = await db.ApiKeys.AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);
        IReadOnlyList<ApiKeySummaryDto> dtos = [.. keys.Select(k => new ApiKeySummaryDto(
            Id: k.Id.ToString(),
            Name: k.Name,
            KeyPrefix: k.KeyPrefix,
            Scopes: [.. k.Scopes],
            CreatedAt: k.CreatedAt,
            LastUsedAt: k.LastUsedAt,
            ExpiresAt: k.ExpiresAt,
            RevokedAt: k.RevokedAt))];
        return Result.Success(dtos);
    }
}
