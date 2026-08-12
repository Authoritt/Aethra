using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Instances.Commands;

/// <summary>
/// Reconfigura el runtime de una Instance existente (targetVm, ports, volumes, healthcheck) y opcional
/// el flag de auto-deploy. Reutiliza <see cref="CreateInstancePortDto"/>/<see cref="CreateInstanceVolumeDto"/>/
/// <see cref="CreateInstanceHealthcheckDto"/>. Inmutables: template/client/environment/slug. El nuevo
/// runtime se aplica al próximo deploy.
/// </summary>
public sealed record ReconfigureInstanceCommand(
    string InstanceId,
    string? TargetVmId,
    IReadOnlyList<CreateInstancePortDto>? Ports,
    IReadOnlyList<CreateInstanceVolumeDto>? Volumes,
    CreateInstanceHealthcheckDto? Healthcheck,
    bool? AutoDeployOnNewBuild) : ICommand;

internal sealed class ReconfigureInstanceHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<ReconfigureInstanceCommand>
{
    public async Task<Result> Handle(ReconfigureInstanceCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.InstanceId, out var parsed) || parsed.Value.Prefix != "ins")
        {
            return Error.Validation("instance.invalid_id", "ID de instance inválido.");
        }
        var instanceId = new InstanceId(parsed.Value);

        var instance = await db.Instances
            .Include(i => i.Ports)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null)
        {
            return Error.NotFound("instance.not_found", $"Instance '{request.InstanceId}' no existe.");
        }

        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == instance.TemplateId, cancellationToken).ConfigureAwait(false);
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == instance.ClientId, cancellationToken).ConfigureAwait(false);
        if (template is null || client is null)
        {
            return Error.NotFound("instance.parents_missing", "Template o Client de la instance no existe.");
        }

        // Ports
        var mappedPorts = new List<PortMapping>();
        foreach (var p in request.Ports ?? [])
        {
            var portResult = Port.Create(p.containerPort);
            if (portResult.IsFailure)
            {
                return portResult.Error;
            }
            // Mismo criterio que en la creación: sin protocolo se asume TCP, pero uno escrito y no
            // reconocido se rechaza en vez de caer a UDP en silencio.
            var protocol = PortProtocol.Tcp;
            if (!string.IsNullOrWhiteSpace(p.protocol)
                && !PortMapping.TryParseProtocol(p.protocol, out protocol))
            {
                return Error.Validation(
                    "instance.invalid_port_protocol",
                    $"Protocolo '{p.protocol}' no soportado. Válidos: {string.Join(", ", PortMapping.SupportedProtocols)}.");
            }
            if (p.hostPort is { } hostPort && (hostPort < PortMapping.MinPort || hostPort > PortMapping.MaxPort))
            {
                return Error.Validation(
                    "instance.invalid_host_port",
                    $"El puerto del host {hostPort} está fuera de rango ({PortMapping.MinPort}-{PortMapping.MaxPort}).");
            }
            mappedPorts.Add(new PortMapping(portResult.Value, p.hostPort, protocol));
        }

        var mappedVolumes = (request.Volumes ?? [])
            .Select(v => new VolumeMount(v.name, v.containerPath, v.readOnly)).ToArray();

        Healthcheck? healthcheck = request.Healthcheck is null
            ? null
            : new Healthcheck(request.Healthcheck.test, request.Healthcheck.intervalSeconds,
                request.Healthcheck.retries, request.Healthcheck.timeoutSeconds, request.Healthcheck.startPeriodSeconds);

        var now = clock.UtcNow;
        try
        {
            instance.UpdateRuntime(
                string.IsNullOrWhiteSpace(request.TargetVmId) ? instance.TargetVmId : request.TargetVmId!,
                mappedPorts, mappedVolumes, healthcheck, template.Slug.Value, client.Slug, now);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("instance.invalid_runtime", ex.Message);
        }

        if (request.AutoDeployOnNewBuild is { } enabled)
        {
            if (enabled) { instance.EnableAutoDeploy(now); } else { instance.DisableAutoDeploy(now); }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
