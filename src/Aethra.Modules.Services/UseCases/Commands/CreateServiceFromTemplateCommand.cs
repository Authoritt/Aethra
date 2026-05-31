using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Modules.Services.Templates;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

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
    IClock clock)
    : ICommandHandler<CreateServiceFromTemplateCommand, ManagedServiceDetailDto>
{
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

        // Generamos password admin y aplicamos sustituciones del template para resolver
        // ${admin_user} / ${admin_password} en env/command. Las credenciales se persisten
        // cifradas — el plain-text vive solo en RAM durante la creación.
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
                exposedExternally: request.ExposedExternally);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("service.invalid", ex.Message);
        }

        // F5: marcamos Ready directo (dry-run del satélite). El arranque real del contenedor
        // Docker lo hará el satélite cuando F5.5 cablee el comando de satélite. Esto permite
        // probar el flujo de bindings + env vars injection sin Docker corriendo.
        svc.MarkProvisioned(clock.UtcNow);

        db.ManagedServices.Add(svc);
        await db.SaveChangesAsync(cancellationToken);

        return ServiceMappers.ToDetail(svc, bindingsCount: 0);
    }
}
