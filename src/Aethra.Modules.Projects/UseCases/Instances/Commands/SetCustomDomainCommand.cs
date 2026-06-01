using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Settings;
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
/// Setea o limpia el dominio custom de una <c>Instance</c>. La transición controla el flujo:
/// - <c>null → hostname</c>  ⇒ <see cref="CustomDomainRequestedIntegrationEvent"/> (Cloudflare crea CNAME, Proxy crea Route TLS).
/// - <c>hostnameA → hostnameB</c> ⇒ <see cref="CustomDomainRemovedIntegrationEvent"/> del viejo + <see cref="CustomDomainRequestedIntegrationEvent"/> del nuevo.
/// - <c>hostname → null</c> ⇒ <see cref="CustomDomainRemovedIntegrationEvent"/> (vuelve al auto-hostname).
/// El customDomain se valida como <see cref="Hostname"/> antes de tocar el aggregate.
/// </summary>
public sealed record SetCustomDomainCommand(string InstanceId, string? CustomDomain) : ICommand;

internal sealed class SetCustomDomainHandler(
    ProjectsDbContext db,
    IClock clock,
    IBaseDomainProvider baseDomainProvider,
    IOutboxWriter outbox)
    : ICommandHandler<SetCustomDomainCommand>
{
    public async Task<Result> Handle(SetCustomDomainCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.InstanceId, out var parsed) || parsed.Value.Prefix != "ins")
        {
            return Error.Validation("instance.invalid_id", "ID de instance inválido.");
        }
        var instanceId = new InstanceId(parsed.Value);

        var instance = await db.Instances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null)
        {
            return Error.NotFound("instance.not_found", $"Instance '{request.InstanceId}' no existe.");
        }

        // Validar el hostname si viene; null/whitespace ⇒ limpiar.
        string? normalizedDomain = null;
        if (!string.IsNullOrWhiteSpace(request.CustomDomain))
        {
            var hostnameResult = Hostname.Create(request.CustomDomain);
            if (hostnameResult.IsFailure)
            {
                return hostnameResult.Error;
            }
            normalizedDomain = hostnameResult.Value.Value;
        }

        var previous = instance.CustomDomain;
        if (previous == normalizedDomain)
        {
            return Result.Success();
        }

        instance.SetCustomDomain(normalizedDomain, clock.UtcNow);

        // Outbox: encolar events antes del SaveChanges para commit atómico (ver
        // CreateInstanceCommand para el rationale).
        int? primaryPort = instance.Ports.Count > 0 ? instance.Ports[0].ContainerPort.Value : null;
        var baseDomain = await baseDomainProvider.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var cloudflareZoneId = baseDomain?.CloudflareZoneId;

        if (previous is not null)
        {
            await outbox.EnqueueAsync(new CustomDomainRemovedIntegrationEvent(
                InstanceId: instance.Id.ToString(),
                Hostname: previous,
                RemovedAt: clock.UtcNow), cancellationToken).ConfigureAwait(false);
        }

        if (normalizedDomain is not null)
        {
            await outbox.EnqueueAsync(new CustomDomainRequestedIntegrationEvent(
                InstanceId: instance.Id.ToString(),
                Hostname: normalizedDomain,
                CloudflareZoneId: cloudflareZoneId,
                TargetVmId: instance.TargetVmId,
                PrimaryPort: primaryPort,
                RequestedAt: clock.UtcNow), cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Nota: el handler de Proxy ya conoce ContainerName/PrimaryPort desde el evento de
        // Provisioned previo; el evento CustomDomainRequested transporta sólo el hostname nuevo
        // + targetVm + cloudflareZone para el flow Cloudflare. La sincronización de la Route con
        // el customDomain la hace el handler Proxy.InstanceProvisionedHandler en el siguiente
        // redeploy o vía un upgrade handler en F9.6 cuando se cablee Cloudflare.

        return Result.Success();
    }
}
