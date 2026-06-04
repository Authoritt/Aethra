using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Projects.Commands;

/// <summary>
/// Borra un <c>Project</c> en cascada: instancias, templates, clients y sus env vars / secrets
/// (polimórficos por scope, sin FK). Las FK del modelo son <c>Restrict</c>, así que el orden lo
/// garantiza EF al borrar los aggregates trackeados (instancias → templates/clients → project).
///
/// <paramref name="Force"/> es obligatorio si el proyecto tiene instancias desplegadas: borrar
/// el registro NO detiene contenedores ni elimina rutas del proxy (eso lo limpia el caller).
/// </summary>
public sealed record DeleteProjectCommand(string ProjectId, bool Force = false) : ICommand;

internal sealed class DeleteProjectHandler(ProjectsDbContext db)
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

        // EnvVars y Secrets son polimórficos (scope_id sin FK): se borran por scope de cada
        // aggregate del proyecto.
        var scopeIds = new List<string> { projectId.ToString() };
        scopeIds.AddRange(tplIds.Select(t => t.ToString()));
        scopeIds.AddRange(cliIds.Select(c => c.ToString()));
        scopeIds.AddRange(instances.Select(i => i.Id.ToString()));

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.EnvironmentVariables.Where(e => scopeIds.Contains(e.ScopeId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Secrets.Where(s => scopeIds.Contains(s.ScopeId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        db.Instances.RemoveRange(instances);
        db.Templates.RemoveRange(templates);
        db.Clients.RemoveRange(clients);
        db.Projects.Remove(project);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
