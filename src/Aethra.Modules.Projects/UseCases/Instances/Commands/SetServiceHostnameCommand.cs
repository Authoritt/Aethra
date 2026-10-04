using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.Infrastructure.Lookups;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Proxy;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Instances.Commands;

/// <summary>
/// OT-009 — fija, cambia o quita el hostname público de UN servicio del template en una
/// <c>Instance</c>. El host efectivo del servicio pasa a ser <c>override ?? svc.Hostname interpolado</c>
/// (rutas, ingress Direct, DNS y monitores lo resuelven en <c>ServiceHostRules</c>).
/// <paramref name="Hostname"/> null/whitespace = quitar el override.
/// <para>
/// Validaciones: el servicio debe existir en el template (un override a un nombre mal escrito no
/// haría nada y nadie lo notaría); el host es un FQDN (<see cref="Hostname.Create"/>) y no wildcard.
/// G2: el host NO puede estar en uso — ni por otra instancia (CustomDomain, AutoHostname, override),
/// ni por una ruta del proxy de otro dueño (p.ej. <c>api.inonetickets.lat</c> de prod), ni por el host
/// raíz de esta misma instancia, ni por otro servicio de ella. Un typo así, al borrar staging, se
/// llevaba por delante rutas y DNS de producción.
/// Si cambia algo se encola <see cref="InstanceServiceHostnameChangedIntegrationEvent"/> en la misma
/// transacción: el host reconcilia el routing (como con el dominio custom).
/// </para>
/// </summary>
public sealed record SetServiceHostnameCommand(string InstanceId, string ServiceName, string? Hostname) : ICommand;

internal sealed class SetServiceHostnameHandler(
    ProjectsDbContext db,
    IClock clock,
    IOutboxWriter<ProjectsDbContext> outbox,
    IRouteHostLookup routeHosts)
    : ICommandHandler<SetServiceHostnameCommand>
{
    public async Task<Result> Handle(SetServiceHostnameCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.InstanceId, out var parsed) || parsed.Value.Prefix != "ins")
        {
            return Error.Validation("instance.invalid_id", "ID de instance inválido.");
        }
        if (string.IsNullOrWhiteSpace(request.ServiceName))
        {
            return Error.Validation("instance.service_hostname.service_required", "Falta el nombre del servicio.");
        }

        string? normalizedHost = null;
        if (!string.IsNullOrWhiteSpace(request.Hostname))
        {
            var hostname = Hostname.Create(request.Hostname);
            if (hostname.IsFailure)
            {
                return hostname.Error;
            }
            if (hostname.Value.IsWildcard)
            {
                return Error.Validation(
                    "instance.service_hostname.wildcard",
                    "El hostname de un servicio no puede ser wildcard: es el host concreto que se publica.");
            }
            normalizedHost = hostname.Value.Value;
        }

        var instanceId = new InstanceId(parsed.Value);
        var instance = await db.Instances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null)
        {
            return Error.NotFound("instance.not_found", $"Instance '{request.InstanceId}' no existe.");
        }

        var template = await db.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == instance.TemplateId, cancellationToken)
            .ConfigureAwait(false);
        var requested = request.ServiceName.Trim();
        var service = template?.Services.FirstOrDefault(
            s => string.Equals(s.Name, requested, StringComparison.OrdinalIgnoreCase));

        // Quitar un override cuyo servicio ya no existe en el template debe poder hacerse (limpieza);
        // FIJAR uno exige que el servicio exista.
        var serviceName = service?.Name
            ?? instance.ServiceHostnames.FirstOrDefault(
                o => string.Equals(o.ServiceName, requested, StringComparison.OrdinalIgnoreCase))?.ServiceName;
        if (normalizedHost is not null && service is null)
        {
            var known = template is null || template.Services.Count == 0
                ? "el template no define servicios"
                : "servicios: " + string.Join(", ", template.Services.Select(s => s.Name));
            return Error.Validation(
                "instance.service_hostname.unknown_service",
                $"El template de la instance no tiene un servicio '{requested}' ({known}).");
        }
        if (serviceName is null)
        {
            return Result.Success(); // quitar algo que no existe: no-op idempotente
        }

        if (normalizedHost is not null)
        {
            var conflict = await FindHostConflictAsync(instance, serviceName, normalizedHost, cancellationToken)
                .ConfigureAwait(false);
            if (conflict is not null)
            {
                return Error.Conflict(
                    "instance.service_hostname.in_use",
                    $"El host '{normalizedHost}' ya está en uso: {conflict}. Un override es exclusivo de su instancia.");
            }
        }

        if (!instance.SetServiceHostname(serviceName, normalizedHost, clock.UtcNow, out var previous))
        {
            return Result.Success();
        }

        await outbox.EnqueueAsync(new InstanceServiceHostnameChangedIntegrationEvent(
            InstanceId: instance.Id.ToString(),
            ServiceName: serviceName,
            Hostname: normalizedHost,
            PreviousHostname: previous,
            ChangedAt: clock.UtcNow), cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>Motivo por el que <paramref name="host"/> no es exclusivo de este servicio, o null.</summary>
    private async Task<string?> FindHostConflictAsync(
        Instance instance, string serviceName, string host, CancellationToken ct)
    {
        if (string.Equals(instance.CustomDomain, host, StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.AutoHostname, host, StringComparison.OrdinalIgnoreCase))
        {
            return "es el host raíz de esta misma instancia";
        }
        var sibling = instance.ServiceHostnames.FirstOrDefault(o =>
            !string.Equals(o.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(o.Hostname, host, StringComparison.OrdinalIgnoreCase));
        if (sibling is not null)
        {
            return $"lo tiene el servicio '{sibling.ServiceName}' de esta instancia";
        }

        var others = await db.Instances.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var other = others.FirstOrDefault(i => i.Id != instance.Id && EfHostnameUsageLookup.UsesHostname(i, host));
        if (other is not null)
        {
            return $"lo usa la instance '{other.Slug}' ({other.Id})";
        }

        var myId = instance.Id.ToString();
        var foreignRoute = (await routeHosts.FindByHostnameAsync(host, ct).ConfigureAwait(false))
            .FirstOrDefault(r => !string.Equals(r.OperationalOwnerId, myId, StringComparison.Ordinal));
        if (foreignRoute is not null)
        {
            return foreignRoute.OperationalOwnerId is null
                ? $"hay una ruta del proxy sin dueño en {foreignRoute.Hostname}{foreignRoute.PathPrefix}"
                : $"hay una ruta del proxy de la instance '{foreignRoute.OperationalOwnerId}' en {foreignRoute.Hostname}{foreignRoute.PathPrefix}";
        }
        return null;
    }
}
