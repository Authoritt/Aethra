using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Templates.Commands;

/// <summary>
/// Borra un Template. Si tiene Instances, requiere <paramref name="Force"/> (cascada): borra las
/// instancias (emitiendo <c>InstanceRemoved</c> para que Proxy/Cloudflare limpien rutas/DNS) y sus
/// env vars/secrets, y el template. Mismo patrón que DeleteProject (execution strategy + tx).
/// </summary>
public sealed record DeleteTemplateCommand(string TemplateId, bool Force = false) : ICommand;

internal sealed class DeleteTemplateHandler(
    ProjectsDbContext db, IClock clock, IOutboxWriter<ProjectsDbContext> outbox)
    : ICommandHandler<DeleteTemplateCommand>
{
    public async Task<Result> Handle(DeleteTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.TemplateId, out var parsed) || parsed.Value.Prefix != "tpl")
        {
            return Error.Validation("template.invalid_id", "ID de template inválido.");
        }
        var templateId = new TemplateId(parsed.Value);

        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound("template.not_found", $"Template '{request.TemplateId}' no existe.");
        }

        var instances = await db.Instances.Where(i => i.TemplateId == templateId).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (instances.Count > 0 && !request.Force)
        {
            return Error.Conflict(
                "template.has_instances",
                $"El template tiene {instances.Count} instancia(s). Confirma el borrado en cascada (force).");
        }

        foreach (var inst in instances)
        {
            await outbox.EnqueueAsync(new InstanceRemovedIntegrationEvent(
                InstanceId: inst.Id.ToString(),
                AutoHostname: inst.AutoHostname,
                CustomDomain: inst.CustomDomain,
                RemovedAt: clock.UtcNow), cancellationToken).ConfigureAwait(false);
        }

        var scopeIds = new List<string> { templateId.ToString() };
        scopeIds.AddRange(instances.Select(i => i.Id.ToString()));

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await db.EnvironmentVariables.Where(e => scopeIds.Contains(e.ScopeId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Secrets.Where(s => scopeIds.Contains(s.ScopeId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            db.Instances.RemoveRange(instances);
            db.Templates.Remove(template);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return Result.Success();
    }
}
