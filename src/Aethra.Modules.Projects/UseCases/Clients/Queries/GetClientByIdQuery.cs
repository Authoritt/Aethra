using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Clients.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Clients.Queries;

/// <summary>
/// Devuelve el detalle de un <c>Client</c> por ID.
/// </summary>
public sealed record GetClientByIdQuery(string ClientId) : IQuery<ClientDetail>;

internal sealed class GetClientByIdHandler(ProjectsDbContext db)
    : IQueryHandler<GetClientByIdQuery, ClientDetail>
{
    public async Task<Result<ClientDetail>> Handle(
        GetClientByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ClientId, out var parsed) || parsed.Value.Prefix != "cli")
        {
            return Error.Validation("client.invalid_id", "ID de client inválido.");
        }
        var clientId = new ClientId(parsed.Value);

        var c = await db.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == clientId, cancellationToken)
            .ConfigureAwait(false);

        if (c is null)
        {
            return Error.NotFound("client.not_found", $"Client '{request.ClientId}' no existe.");
        }

        return new ClientDetail(
            id: c.Id.ToString(),
            projectId: c.ProjectId.ToString(),
            slug: c.Slug,
            displayName: c.DisplayName,
            description: c.Description,
            contactEmail: c.ContactEmail,
            billingTag: c.BillingTag,
            createdAt: c.CreatedAt,
            updatedAt: c.UpdatedAt);
    }
}
