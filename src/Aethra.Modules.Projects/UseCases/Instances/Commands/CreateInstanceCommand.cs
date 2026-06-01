using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Instances.Dtos;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Settings;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Instances.Commands;

/// <summary>
/// Crea una <c>Instance</c> = Template × Client × Environment en una VM target. El handler:
/// 1) carga Template y Client de la BD para obtener sus slugs (componen <c>ContainerName</c>),
/// 2) valida que el <see cref="Environment"/> sea uno de los configurados en Settings,
/// 3) instancia el aggregate via <see cref="Instance.Create"/>,
/// 4) si hay BaseDomain activo, calcula el auto-hostname y lo persiste,
/// 5) encola <see cref="InstanceProvisionedIntegrationEvent"/> en outbox para que Proxy/Cloudflare
///    reaccionen y mantengan sync sus rutas y registros DNS.
/// </summary>
public sealed record CreateInstanceCommand(
    string TemplateId,
    string ClientId,
    string Environment,
    string TargetVmId,
    string? SlugOverride,
    IReadOnlyList<CreateInstancePortDto>? Ports,
    IReadOnlyList<CreateInstanceVolumeDto>? Volumes,
    CreateInstanceHealthcheckDto? Healthcheck,
    bool AutoDeployOnNewBuild) : ICommand<InstanceDetail>;

public sealed record CreateInstancePortDto(int containerPort, int? hostPort, string? protocol);

public sealed record CreateInstanceVolumeDto(string name, string containerPath, bool readOnly);

public sealed record CreateInstanceHealthcheckDto(
    IReadOnlyList<string> test,
    int intervalSeconds,
    int retries,
    int? timeoutSeconds,
    int? startPeriodSeconds);

public sealed class CreateInstanceValidator : AbstractValidator<CreateInstanceCommand>
{
    public CreateInstanceValidator()
    {
        RuleFor(c => c.TemplateId).NotEmpty();
        RuleFor(c => c.ClientId).NotEmpty();
        RuleFor(c => c.Environment).NotEmpty().MaximumLength(32);
        RuleFor(c => c.TargetVmId).NotEmpty().MaximumLength(64);
        RuleFor(c => c.SlugOverride).MaximumLength(64);
    }
}

internal sealed class CreateInstanceHandler(
    ProjectsDbContext db,
    IClock clock,
    IEnvironmentCatalog environmentCatalog,
    IBaseDomainProvider baseDomainProvider,
    IOutboxWriter outbox)
    : ICommandHandler<CreateInstanceCommand, InstanceDetail>
{
    public async Task<Result<InstanceDetail>> Handle(CreateInstanceCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.TemplateId, out var parsedTemplate) || parsedTemplate.Value.Prefix != "tpl")
        {
            return Error.Validation("instance.invalid_template_id", "ID de template inválido.");
        }
        if (!AethraId.TryParse(request.ClientId, out var parsedClient) || parsedClient.Value.Prefix != "cli")
        {
            return Error.Validation("instance.invalid_client_id", "ID de client inválido.");
        }

        var templateId = new TemplateId(parsedTemplate.Value);
        var clientId = new ClientId(parsedClient.Value);

        var template = await db.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            .ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound("instance.template_not_found", $"Template '{request.TemplateId}' no existe.");
        }

        var client = await db.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken)
            .ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound("instance.client_not_found", $"Client '{request.ClientId}' no existe.");
        }

        // Template y Client deben pertenecer al mismo Project — invariante del modelo F9.
        if (template.ProjectId != client.ProjectId)
        {
            return Error.Validation(
                "instance.cross_project",
                "El Template y el Client deben pertenecer al mismo proyecto.");
        }

        // Environment debe estar registrado en Settings (catalog dinámico).
        if (!await environmentCatalog.IsValidAsync(request.Environment, cancellationToken).ConfigureAwait(false))
        {
            return Error.Validation(
                "instance.invalid_environment",
                $"Environment '{request.Environment}' no está registrado. Configúralo en Settings.");
        }

        // Verificar duplicado por slug derivado dentro del mismo Template (índice unique
        // ux_instances_template_slug en BD; aquí damos un mensaje claro antes del 23505).
        var derivedSlug = string.IsNullOrWhiteSpace(request.SlugOverride)
            ? $"{client.Slug}-{request.Environment.Trim().ToLowerInvariant()}"
            : request.SlugOverride.Trim().ToLowerInvariant();
        if (await db.Instances
                .AnyAsync(i => i.TemplateId == templateId && i.Slug == derivedSlug, cancellationToken)
                .ConfigureAwait(false))
        {
            return Error.Conflict(
                "instance.slug_taken",
                $"Ya existe una instance con slug '{derivedSlug}' en este template.");
        }

        // Mapear DTOs a value objects del dominio.
        var portsResult = MapPorts(request.Ports);
        if (portsResult.IsFailure)
        {
            return portsResult.Error;
        }

        var volumes = MapVolumes(request.Volumes);
        var healthcheck = MapHealthcheck(request.Healthcheck);

        Instance instance;
        try
        {
            instance = Instance.Create(
                templateId,
                clientId,
                request.Environment,
                request.TargetVmId,
                template.Slug.Value,
                client.Slug,
                portsResult.Value,
                volumes,
                healthcheck,
                request.AutoDeployOnNewBuild,
                clock.UtcNow,
                request.SlugOverride);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("instance.invalid", ex.Message);
        }

        // Auto-hostname: {template.slug}-{client.slug}-{environment}.{baseDomain.hostname}.
        // Si no hay BaseDomain configurado, queda null — el Proxy lo ignora con un warning.
        var baseDomain = await baseDomainProvider.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (baseDomain is not null)
        {
            var autoHost = $"{template.Slug.Value}-{client.Slug}-{instance.Environment}.{baseDomain.Hostname}";
            instance.SetAutoHostname(autoHost, clock.UtcNow);
        }

        db.Instances.Add(instance);

        // Outbox: añadimos el integration event ANTES del SaveChanges para que se persista
        // en la misma transacción que la Instance — sin TransactionBehavior global, el commit
        // único garantiza atomicidad. El dispatcher del módulo Projects publica al bus y el
        // handler del Proxy crea la Route YARP idempotentemente (ver InstanceProvisionedHandler).
        int? primaryPort = instance.Ports.Count > 0 ? instance.Ports[0].ContainerPort.Value : null;
        await outbox.EnqueueAsync(new InstanceProvisionedIntegrationEvent(
            InstanceId: instance.Id.ToString(),
            TemplateId: instance.TemplateId.ToString(),
            ClientId: instance.ClientId.ToString(),
            Environment: instance.Environment,
            TargetVmId: instance.TargetVmId,
            ContainerName: instance.ContainerName,
            PrimaryPort: primaryPort,
            AutoHostname: instance.AutoHostname,
            CustomDomain: instance.CustomDomain,
            CreatedAt: instance.CreatedAt), cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Project(instance, client.Slug);
    }

    private static Result<IReadOnlyList<PortMapping>> MapPorts(IReadOnlyList<CreateInstancePortDto>? ports)
    {
        if (ports is null || ports.Count == 0)
        {
            IReadOnlyList<PortMapping> empty = Array.Empty<PortMapping>();
            return Result.Success(empty);
        }
        var mapped = new List<PortMapping>(ports.Count);
        foreach (var p in ports)
        {
            var portResult = Port.Create(p.containerPort);
            if (portResult.IsFailure)
            {
                return portResult.Error;
            }
            var protocol = string.IsNullOrWhiteSpace(p.protocol) || string.Equals(p.protocol, "tcp", StringComparison.OrdinalIgnoreCase)
                ? PortProtocol.Tcp
                : PortProtocol.Udp;
            mapped.Add(new PortMapping(portResult.Value, p.hostPort, protocol));
        }
        IReadOnlyList<PortMapping> result = mapped;
        return Result.Success(result);
    }

    private static VolumeMount[] MapVolumes(IReadOnlyList<CreateInstanceVolumeDto>? volumes)
    {
        if (volumes is null || volumes.Count == 0)
        {
            return [];
        }
        return [.. volumes.Select(v => new VolumeMount(v.name, v.containerPath, v.readOnly))];
    }

    private static Healthcheck? MapHealthcheck(CreateInstanceHealthcheckDto? hc)
    {
        if (hc is null)
        {
            return null;
        }
        return new Healthcheck(hc.test, hc.intervalSeconds, hc.retries, hc.timeoutSeconds, hc.startPeriodSeconds);
    }

    private static InstanceDetail Project(Instance i, string clientSlug)
    {
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
            updatedAt: i.UpdatedAt);
    }
}
