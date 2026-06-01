using Aethra.Modules.Settings.Infrastructure;
using Aethra.Modules.Settings.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Settings.UseCases.IntegrationCredentials.Queries;

public sealed record ListIntegrationCredentialsQuery : IQuery<IReadOnlyList<IntegrationCredentialDto>>;

internal sealed class ListIntegrationCredentialsHandler(SettingsDbContext db)
    : IQueryHandler<ListIntegrationCredentialsQuery, IReadOnlyList<IntegrationCredentialDto>>
{
    public async Task<Result<IReadOnlyList<IntegrationCredentialDto>>> Handle(
        ListIntegrationCredentialsQuery request,
        CancellationToken cancellationToken)
    {
        var rows = await db.IntegrationCredentials
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<IntegrationCredentialDto> dtos = [.. rows.Select(Mappers.ToDto)];
        return Result.Success(dtos);
    }
}
