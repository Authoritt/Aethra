using Aethra.Modules.Projects.Domain;
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

namespace Aethra.Modules.Projects.UseCases.Projects.Commands;

/// <summary>
/// Borra un <c>Project</c> en cascada: instancias, templates, clients y sus env vars / secrets
/// (polimórficos por scope, sin FK). Las FK del modelo son <c>Restrict</c>, así que el orden lo
/// garantiza EF al borrar los aggregates trackeados (instancias → templates/clients → project).
///
/// <paramref name="Force"/> es obligatorio si el proyecto tiene instancias desplegadas: borrar
/// en cascada emite <c>InstanceRemoved</c> para que Proxy/Cloudflare/Monitoring/Deployments limpien
/// rutas, DNS, monitores y contenedores.
/// </summary>
public sealed record DeleteProjectCommand(string ProjectId, bool Force = false) : ICommand;

internal sealed class DeleteProjectHandler(
    ProjectsDbContext db,
    IClock clock,
    IOutboxWriter<ProjectsDbContext> outbox)
    : ICommandHandler<DeleteProjectCommand>
{
    public async Task<Result> Handle(DeleteProjectCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.ProjectId, out var parsed) || parsed.Value.Prefix != "prj")
        {
            return Error.Validation("project.invalid_id", "ID de proyecto inválido.");
        }
        var projectId = new ProjectId(parsed.Value);

        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return Error.NotFound("project.not_found", $"Proyecto '{request.ProjectId}' no existe.");
        }

        var templates = await db.Templates.Where(t => t.ProjectId == projectId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var clients = await db.Clients.Where(c => c.ProjectId == projectId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var tplIds = templates.Select(t => t.Id).ToList();
        var cliIds = clients.Select(c => c.Id).ToList();
        var instances = await db.Instances
            .Where(i => tplIds.Contains(i.TemplateId) || cliIds.Contains(i.ClientId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (instances.Count > 0 && !request.Force)
        {
            return Error.Conflict(
                "project.has_instances",
                $"El proyecto tiene {instances.Count} instancia(s) desplegada(s). Confirma el borrado en cascada (force).");
        }

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

        // EnvVars y Secrets son polimórficos (scope_id sin FK): se borran por scope de cada
        // aggregate del proyecto.
        var scopeIds = new List<string> { projectId.ToString() };
        scopeIds.AddRange(tplIds.Select(t => t.ToString()));
        scopeIds.AddRange(cliIds.Select(c => c.ToString()));
        scopeIds.AddRange(instances.Select(i => i.Id.ToString()));

        // El DbContext usa NpgsqlRetryingExecutionStrategy, que exige que las transacciones
        // manuales se ejecuten dentro de la estrategia (unidad reintentable).
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await DeleteScopedRowsAsync(scopeIds, cancellationToken).ConfigureAwait(false);

            db.Instances.RemoveRange(instances);
            db.Templates.RemoveRange(templates);
            db.Clients.RemoveRange(clients);
            db.Projects.Remove(project);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return Result.Success();
    }

    private async Task DeleteScopedRowsAsync(IReadOnlyCollection<string> scopeIds, CancellationToken cancellationToken)
    {
        // Un solo camino, el de producción. La versión anterior se bifurcaba con
        // `if (db.Database.IsRelational())` y la rama no-relacional existía SOLO para que los tests
        // (EF InMemory) no reventaran, porque ese proveedor no implementa ExecuteDelete. El efecto
        // era que la suite recorría un camino que producción nunca ejecuta: un verde que no probaba
        // nada de lo que aquí se promete.
        //
        // OJO — hoy esta línea SIGUE sin cobertura, y no por descuido: montar un test relacional
        // está bloqueado por la política de paquetes del repo (ver issue #105). Ningún test siembra
        // filas con scope, así que este método se ejecuta siempre sobre conjunto vacío. Lo que se
        // gana con este cambio es que el código diga la verdad: hay un único comportamiento, el real.
        await db.EnvironmentVariables.Where(e => scopeIds.Contains(e.ScopeId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await db.Secrets.Where(s => scopeIds.Contains(s.ScopeId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
