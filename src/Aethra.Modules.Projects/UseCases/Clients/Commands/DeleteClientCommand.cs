using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Clients.Commands;

/// <summary>
/// Borra un <c>Client</c>. Si tiene instancias asociadas requiere <paramref name="Force"/>: con force
/// borra en cascada las instancias del client y sus env vars / secrets (polimórficos por scope, sin
/// FK). Borrar el registro NO detiene contenedores ni limpia rutas del proxy (eso lo hace el caller).
/// </summary>
public sealed record DeleteClientCommand(string ClientId, bool Force = false) : ICommand;

internal sealed class DeleteClientHandler(ProjectsDbContext db)
    : ICommandHandler<DeleteClientCommand>
{
    public async Task<Result> Handle(DeleteClientCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ClientId, out var parsed) || parsed.Value.Prefix != "cli")
        {
            return Error.Validation("client.invalid_id", "ID de client inválido.");
        }
        var clientId = new ClientId(parsed.Value);

        var client = await db.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken)
            .ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound("client.not_found", $"Client '{request.ClientId}' no existe.");
        }

        var instances = await db.Instances
            .Where(i => i.ClientId == clientId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (instances.Count > 0 && !request.Force)
        {
            return Error.Conflict(
                "client.has_instances",
                $"El client tiene {instances.Count} instancia(s) asociada(s). Confirma el borrado en cascada (force).");
        }

        var scopeIds = new List<string> { clientId.ToString() };
        scopeIds.AddRange(instances.Select(i => i.Id.ToString()));

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await db.EnvironmentVariables.Where(e => scopeIds.Contains(e.ScopeId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Secrets.Where(s => scopeIds.Contains(s.ScopeId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            db.Instances.RemoveRange(instances);
            db.Clients.Remove(client);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return Result.Success();
    }
}
