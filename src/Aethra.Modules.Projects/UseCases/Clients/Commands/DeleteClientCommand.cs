using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Instances.Commands;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Clients.Commands;

/// <summary>
/// Borra un <c>Client</c>. Si tiene instancias asociadas requiere <paramref name="Force"/>: con force
/// borra en cascada las instancias del client y sus env vars / secrets (polimórficos por scope, sin
/// FK), emitiendo <c>InstanceRemoved</c> para que Proxy/Cloudflare/Monitoring/Deployments limpien
/// rutas, DNS, monitores y contenedores.
/// </summary>
public sealed record DeleteClientCommand(string ClientId, bool Force = false) : ICommand;

internal sealed class DeleteClientHandler(
    ProjectsDbContext db,
    IClock clock,
    IOutboxWriter<ProjectsDbContext> outbox)
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

        var templateIds = instances.Select(i => i.TemplateId).Distinct().ToList();
        var templates = await db.Templates
            .Where(t => templateIds.Contains(t.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var inst in instances)
        {
            var template = templates.FirstOrDefault(t => t.Id == inst.TemplateId);
            await outbox.EnqueueAsync(new InstanceRemovedIntegrationEvent(
                InstanceId: inst.Id.ToString(),
                AutoHostname: inst.AutoHostname,
                CustomDomain: inst.CustomDomain,
                RemovedAt: clock.UtcNow,
                TargetVmId: inst.TargetVmId,
                ContainerNames: DeleteInstanceHandler.ResolveContainerNames(
                    inst.Slug, inst.ContainerName, template)), cancellationToken).ConfigureAwait(false);
        }

        var scopeIds = new List<string> { clientId.ToString() };
        scopeIds.AddRange(instances.Select(i => i.Id.ToString()));

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await DeleteScopedRowsAsync(scopeIds, cancellationToken).ConfigureAwait(false);

            db.Instances.RemoveRange(instances);
            db.Clients.Remove(client);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return Result.Success();
    }

    private async Task DeleteScopedRowsAsync(IReadOnlyCollection<string> scopeIds, CancellationToken cancellationToken)
    {
        // Mismo criterio que DeleteProjectHandler: un solo camino, el de producción. Sin guard de
        // "¿hay filas?" — no aporta nada a producción (dos consultas extra para ahorrar un DELETE
        // que ya sería no-op) y su único efecto real era evitar que EF InMemory reventara en los
        // tests, es decir, garantizar que esta línea nunca se ejercitara. Cobertura real bloqueada
        // por la política de paquetes del repo: issue #105.
        await db.EnvironmentVariables.Where(e => scopeIds.Contains(e.ScopeId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await db.Secrets.Where(s => scopeIds.Contains(s.ScopeId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
