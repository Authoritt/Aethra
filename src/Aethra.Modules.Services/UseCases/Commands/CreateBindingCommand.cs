using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Binding;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.UseCases.Commands;

public sealed record CreateBindingCommand(
    string ServiceId,
    string ApplicationId,
    string? ResourceName,
    BindingPermissions Permissions,
    string? EnvVarPrefix,
    MigrationsHook? MigrationsHook) : ICommand<ServiceBindingDto>;

public sealed class CreateBindingValidator : AbstractValidator<CreateBindingCommand>
{
    public CreateBindingValidator()
    {
        RuleFor(c => c.ServiceId).NotEmpty();
        RuleFor(c => c.ApplicationId).NotEmpty();
        RuleFor(c => c.ResourceName)
            .Matches("^[a-zA-Z_][a-zA-Z0-9_]{0,62}$")
            .When(c => !string.IsNullOrWhiteSpace(c.ResourceName))
            .WithMessage("ResourceName solo admite letras, dígitos y _ (Postgres-safe identifier).");
    }
}

internal sealed class CreateBindingHandler(
    ServicesDbContext db,
    IApplicationLookup appLookup,
    IEnumerable<IServiceProvisioner> provisioners,
    IBindingCredentialsCodec bindingCodec,
    IEnvVarWriter envVarWriter,
    IBindingEnvVarMapper envVarMapper,
    IClock clock,
    ILogger<CreateBindingHandler> logger)
    : ICommandHandler<CreateBindingCommand, ServiceBindingDto>
{
    public async Task<Result<ServiceBindingDto>> Handle(CreateBindingCommand request, CancellationToken cancellationToken)
    {
        var svc = await db.ManagedServices.FirstOrDefaultAsync(
            s => s.Id.ToString() == request.ServiceId, cancellationToken);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", $"ManagedService '{request.ServiceId}' no existe.");
        }
        if (svc.Status != ManagedServiceStatus.Ready)
        {
            return Error.Conflict("service.not_ready",
                $"El servicio está en estado '{svc.Status}'. Debe estar 'Ready' para crear bindings.");
        }

        var app = await appLookup.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (app is null)
        {
            return Error.NotFound("application.not_found", $"Application '{request.ApplicationId}' no existe.");
        }

        // Si ya existe binding activo para (svc, app) — convención: uno solo activo a la vez.
        var existing = await db.ServiceBindings.AnyAsync(
            b => b.ServiceId == svc.Id && b.ApplicationId == request.ApplicationId && b.RevokedAt == null,
            cancellationToken);
        if (existing)
        {
            return Error.Conflict("binding.duplicate",
                "Ya existe un binding activo para esta application+service. Revoca el anterior primero.");
        }

        var resourceName = (request.ResourceName ?? app.Slug).Trim();
        var username = $"{resourceName}_user";
        var password = CredentialsGenerator.GeneratePassword(32);
        var newCreds = new BindingCredentials(username, password);
        var encrypted = bindingCodec.Encode(newCreds);

        ServiceBinding binding;
        try
        {
            binding = ServiceBinding.Create(svc.Id, request.ApplicationId, resourceName, encrypted,
                request.Permissions, request.EnvVarPrefix, request.MigrationsHook, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("binding.invalid", ex.Message);
        }

        // Llama al provisioner del tipo del servicio. Si no hay implementación, F5 acepta
        // dry-run y deja el binding como "no provisionado" — útil para validar wiring.
        var provisioner = provisioners.FirstOrDefault(p => p.SupportedType == svc.Type);
        if (provisioner is not null)
        {
            var outcome = await provisioner.ProvisionAsync(svc, binding, newCreds, cancellationToken);
            if (!outcome.Success)
            {
                logger.LogWarning("Provisioner falló para {Type}: {Code} {Msg}",
                    svc.Type, outcome.ErrorCode, outcome.ErrorMessage);
                return Error.Conflict(outcome.ErrorCode ?? "provision_failed",
                    outcome.ErrorMessage ?? "Provisioning falló");
            }
            binding.MarkProvisioned(clock.UtcNow);
        }

        db.ServiceBindings.Add(binding);

        // Inyectar env vars en la Application (DATABASE_URL, *_USER, *_PASSWORD, etc.).
        var envVars = envVarMapper.Build(svc, binding, newCreds);
        await envVarWriter.UpsertManyAsync(request.ApplicationId,
            source: $"binding:{binding.Id}",
            vars: envVars,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ServiceMappers.ToDto(binding, app.Slug);
    }
}
