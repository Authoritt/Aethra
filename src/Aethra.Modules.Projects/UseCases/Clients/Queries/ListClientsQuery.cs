using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Clients.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Clients.Queries;

/// <summary>
/// Lista los <c>Client</c>s de un <c>Project</c>, ordenados por <c>DisplayName</c>.
/// </summary>
public sealed record ListClientsQuery(string ProjectId) : IQuery<IReadOnlyList<ClientSummary>>;

internal sealed class ListClientsHandler(ProjectsDbContext db)
    : IQueryHandler<ListClientsQuery, IReadOnlyList<ClientSummary>>
{
    public async Task<Result<IReadOnlyList<ClientSummary>>> Handle(
        ListClientsQuery request,
        CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ProjectId, out var parsed) || parsed.Value.Prefix != "prj")
        {
            return Error.Validation("client.invalid_project_id", "ID de proyecto inválido.");
        }
        var projectId = new ProjectId(parsed.Value);

        var rows = await db.Clients
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ClientSummary> dtos = [.. rows.Select(c => new ClientSummary(
            id: c.Id.ToString(),
            projectId: c.ProjectId.ToString(),
            slug: c.Slug,
            displayName: c.DisplayName,
            description: c.Description,
            contactEmail: c.ContactEmail,
            billingTag: c.BillingTag,
            createdAt: c.CreatedAt,
            updatedAt: c.UpdatedAt))];

        return Result.Success(dtos);
    }
}
