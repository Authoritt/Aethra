using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Commands;

/// <summary>
/// Adopta un servicio (Postgres/Redis/...) que YA existe como contenedor (creado fuera de Aethra)
/// para que aparezca en /services y /data-services SIN provisionarlo ni recrearlo. No arranca ni
/// toca el contenedor: solo registra metadata + credenciales admin (cifradas) apuntando al existente.
/// </summary>
public sealed record AdoptServiceCommand(
    string Slug,
    string Name,
    string Type,
    string Version,
    string TargetVmId,
    string ContainerName,
    string Image,
    int InternalPort,
    string NetworkName,
    string AdminUser,
    string AdminPassword,
    bool ExposedExternally) : ICommand<ManagedServiceDetailDto>;

internal sealed class AdoptServiceHandler(
    ServicesDbContext db,
    IAdminCredentialsCodec codec,
    IClock clock)
    : ICommandHandler<AdoptServiceCommand, ManagedServiceDetailDto>
{
    public async Task<Result<ManagedServiceDetailDto>> Handle(AdoptServiceCommand request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ServiceType>(request.Type, ignoreCase: true, out var type))
        {
            return Error.Validation("service.invalid_type",
                $"Tipo inválido: '{request.Type}'. Use Postgres|Redis|RabbitMQ|MySQL|MongoDB|MariaDB|ClickHouse.");
        }
        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            return Error.Validation("service.invalid", "Slug requerido.");
        }
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await db.ManagedServices.AnyAsync(s => s.Slug == slug, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("service.slug_taken", $"Ya existe un servicio con slug '{slug}'.");
        }

        // Las creds admin del servicio existente se cifran en reposo (purpose aethra-svc-admin).
        // Redis sin auth: se guarda un placeholder no vacío (el codec exige campos no vacíos).
        var creds = new AdminCredentials(
            string.IsNullOrWhiteSpace(request.AdminUser) ? "admin" : request.AdminUser.Trim(),
            string.IsNullOrWhiteSpace(request.AdminPassword) ? "(none)" : request.AdminPassword);
        var cipher = codec.Encode(creds);

        ManagedService svc;
        try
        {
            svc = ManagedService.Adopt(
                slug: slug,
                name: string.IsNullOrWhiteSpace(request.Name) ? slug : request.Name,
                type: type,
                version: string.IsNullOrWhiteSpace(request.Version) ? "external" : request.Version,
                targetVmId: request.TargetVmId,
                containerName: request.ContainerName,
                image: request.Image,
                internalPort: request.InternalPort > 0 ? request.InternalPort : type.DefaultInternalPort(),
                networkName: request.NetworkName,
                adminCredentialsCipher: cipher,
                now: clock.UtcNow,
                exposedExternally: request.ExposedExternally);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("service.invalid", ex.Message);
        }

        db.ManagedServices.Add(svc);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ServiceMappers.ToDetail(svc, bindingsCount: 0);
    }
}
