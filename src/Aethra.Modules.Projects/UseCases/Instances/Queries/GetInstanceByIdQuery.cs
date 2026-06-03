using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Instances.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Instances.Queries;

/// <summary>
/// Devuelve el detalle completo de una <c>Instance</c>: ports, volumes, healthcheck, hostnames.
/// </summary>
public sealed record GetInstanceByIdQuery(string InstanceId) : IQuery<InstanceDetail>;

internal sealed class GetInstanceByIdHandler(ProjectsDbContext db)
    : IQueryHandler<GetInstanceByIdQuery, InstanceDetail>
{
    public async Task<Result<InstanceDetail>> Handle(
        GetInstanceByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.InstanceId, out var parsed) || parsed.Value.Prefix != "ins")
        {
            return Error.Validation("instance.invalid_id", "ID de instance inválido.");
        }
        var instanceId = new InstanceId(parsed.Value);

        var i = await db.Instances
            .AsNoTracking()
            .Include(x => x.Ports)
            .Include(x => x.Volumes)
            .FirstOrDefaultAsync(x => x.Id == instanceId, cancellationToken)
            .ConfigureAwait(false);

        if (i is null)
        {
            return Error.NotFound("instance.not_found", $"Instance '{request.InstanceId}' no existe.");
        }

        // Resolver el slug del Client para mostrar nombre amigable en la UI.
        var clientSlug = await db.Clients
            .AsNoTracking()
            .Where(c => c.Id == i.ClientId)
            .Select(c => c.Slug)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? string.Empty;

        IReadOnlyList<InstancePortDto> ports =
            [.. i.Ports.Select(p => new InstancePortDto(p.ContainerPort.Value, p.HostPort, p.Protocol.ToString()))];
        IReadOnlyList<InstanceVolumeDto> volumes =
            [.. i.Volumes.Select(v => new InstanceVolumeDto(v.Name, v.ContainerPath, v.ReadOnly))];
        InstanceHealthcheckDto? hc = i.Healthcheck is null
            ? null
            : new InstanceHealthcheckDto(
                i.Healthcheck.Test,
                i.Healthcheck.IntervalSeconds,
                i.Healthcheck.Retries,
                i.Healthcheck.TimeoutSeconds,
                i.Healthcheck.StartPeriodSeconds);

        // F12.3 — resolver EffectiveTrackedRef cargando el Template (con EnvironmentMapping).
        var template = await db.Templates
            .AsNoTracking()
            .Include(t => t.EnvironmentMapping)
            .FirstOrDefaultAsync(t => t.Id == i.TemplateId, cancellationToken)
            .ConfigureAwait(false);
        var effectiveRef = template is not null ? i.ResolveTrackedRef(template) : null;

        return new InstanceDetail(
            id: i.Id.ToString(),
            templateId: i.TemplateId.ToString(),
            clientId: i.ClientId.ToString(),
            clientSlug: clientSlug,
            environment: i.Environment,
            slug: i.Slug,
            targetVmId: i.TargetVmId,
            containerName: i.ContainerName,
            autoDeployOnNewBuild: i.AutoDeployOnNewBuild,
            customDomain: i.CustomDomain,
            autoHostname: i.AutoHostname,
            ports: ports,
            volumes: volumes,
            healthcheck: hc,
            createdAt: i.CreatedAt,
            updatedAt: i.UpdatedAt,
            trackedRef: i.TrackedRef,
            effectiveTrackedRef: effectiveRef,
            isEphemeral: i.IsEphemeral,
            expiresAt: i.ExpiresAt,
            createdByUserId: i.CreatedByUserId);
    }
}
