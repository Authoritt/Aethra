using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Modules.Services.Templates;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aethra.Modules.Services.UseCases.Commands;

public sealed record CreateServiceFromTemplateCommand(
    string TemplateId,
    string Slug,
    string Name,
    string TargetVmId,
    bool ExposedExternally) : ICommand<ManagedServiceDetailDto>;

public sealed class CreateServiceFromTemplateValidator : AbstractValidator<CreateServiceFromTemplateCommand>
{
    public CreateServiceFromTemplateValidator()
    {
        RuleFor(c => c.TemplateId).NotEmpty();
        RuleFor(c => c.Slug).NotEmpty().MaximumLength(64)
            .Matches("^[a-z][a-z0-9-]{0,30}$")
            .WithMessage("Slug debe ser lowercase y contener solo a-z 0-9 y guiones.");
        RuleFor(c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(c => c.TargetVmId).NotEmpty().MaximumLength(64);
    }
}

internal sealed class CreateServiceFromTemplateHandler(
    ServicesDbContext db,
    IServiceTemplateCatalog catalog,
    IAdminCredentialsCodec codec,
    ISatelliteRpcClient satellite,
    IConfiguration configuration,
    IClock clock)
    : ICommandHandler<CreateServiceFromTemplateCommand, ManagedServiceDetailDto>
{
    // Red Docker compartida (misma que usan las apps en el módulo Deployments) para que un
    // contenedor de aplicación pueda alcanzar el servicio por nombre DNS (host = ContainerName).
    private readonly string _appNetwork =
        configuration["Deployments:AppNetwork"] is { Length: > 0 } n ? n : "aethra-net";

    public async Task<Result<ManagedServiceDetailDto>> Handle(CreateServiceFromTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = catalog.GetById(request.TemplateId);
        if (template is null)
        {
            return Error.Validation("template.unknown", $"Plantilla '{request.TemplateId}' no existe.");
        }

        if (await db.ManagedServices.AnyAsync(s => s.Slug == request.Slug, cancellationToken))
        {
            return Error.Conflict("service.slug_taken", $"Ya existe un servicio con slug '{request.Slug}'.");
        }

        // Generamos password admin. Las credenciales se persisten cifradas — el plain-text vive
        // solo en RAM durante la creación, justo para interpolar el template y arrancar.
        var adminPassword = CredentialsGenerator.GeneratePassword(32);
        var adminCreds = new AdminCredentials(template.AdminUser, adminPassword);
        var cipher = codec.Encode(adminCreds);

        ManagedService svc;
        try
        {
            svc = ManagedService.Create(
                slug: request.Slug,
                name: request.Name,
                type: template.Type,
                version: template.Version,
                targetVmId: request.TargetVmId,
                image: template.Image,
                adminCredentialsCipher: cipher,
                now: clock.UtcNow,
                internalPortOverride: template.InternalPort,
                exposedExternally: request.ExposedExternally,
                networkName: _appNetwork);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("service.invalid", ex.Message);
        }

        // F10.1b: arranque real del contenedor del servicio en el satélite. Interpolamos
        // ${admin_user}/${admin_password} en env y command con las credenciales generadas.
        var subs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["admin_user"] = template.AdminUser,
            ["admin_password"] = adminPassword,
        };
        var spec = BuildRunSpec(svc, template, subs);

        try
        {
            var result = await satellite.SendRunAsync(svc.TargetVmId, spec, pullFrom: null, ct: cancellationToken);
            if (result.Success && !string.IsNullOrWhiteSpace(result.ContainerId))
            {
                svc.MarkProvisioned(clock.UtcNow);
            }
            else
            {
                svc.MarkFailed("provision_failed",
                    result.ErrorMessage ?? "El satélite no pudo arrancar el contenedor del servicio.",
                    clock.UtcNow);
            }
        }
        catch (SatelliteNotConnectedException ex)
        {
            svc.MarkFailed("no_satellite", ex.Message, clock.UtcNow);
        }
        catch (TimeoutException ex)
        {
            svc.MarkFailed("satellite_timeout", ex.Message, clock.UtcNow);
        }

        db.ManagedServices.Add(svc);
        await db.SaveChangesAsync(cancellationToken);

        if (svc.Status == ManagedServiceStatus.Failed)
        {
            return Error.Failure(svc.ErrorCode ?? "service.provision_failed",
                svc.ErrorMessage ?? "El provisioning del servicio falló.");
        }

        return ServiceMappers.ToDetail(svc, bindingsCount: 0);
    }

    /// <summary>
    /// Construye el <see cref="RunSpec"/> del contenedor del servicio a partir de la plantilla:
    /// env/command interpolados, volúmenes named por servicio, healthcheck y red compartida. Los
    /// puertos solo se publican al host si el servicio se expone externamente; en caso contrario
    /// queda accesible únicamente por la red interna (host = ContainerName).
    /// </summary>
    private RunSpec BuildRunSpec(ManagedService svc, ServiceTemplate template,
        IReadOnlyDictionary<string, string> subs)
    {
        var env = TemplateInterpolator.Apply(template.Env, subs);
        var command = TemplateInterpolator.Apply(template.Command, subs);

        var volumes = template.Volumes
            .Select(v => new VolumeBinding(
                NameOrHostPath: $"{svc.Slug}-{v.Name}",
                ContainerPath: v.ContainerPath,
                ReadOnly: false))
            .ToList();

        var ports = new List<PortBinding>();
        if (svc.ExposedExternally)
        {
            ports.Add(new PortBinding(template.InternalPort, HostPort: null, Protocol: "tcp"));
            if (template.ManagementPort is int mp)
            {
                ports.Add(new PortBinding(mp, HostPort: null, Protocol: "tcp"));
            }
        }

        HealthcheckSpec? hc = template.Healthcheck is { } h
            ? new HealthcheckSpec(h.Test, h.IntervalSeconds, h.Retries,
                TimeoutSeconds: null, StartPeriodSeconds: null)
            : null;

        return new RunSpec(
            ContainerName: svc.ContainerName,
            ImageRef: svc.Image,
            Env: env,
            Ports: ports,
            Volumes: volumes,
            Command: command,
            Healthcheck: hc,
            NetworkName: svc.NetworkName,
            RestartPolicy: "unless-stopped");
    }
}
