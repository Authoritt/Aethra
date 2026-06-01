using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Binding;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Services.UseCases.Commands;

public sealed record RotateCredentialsCommand(string BindingId) : ICommand;

internal sealed class RotateCredentialsHandler(
    ServicesDbContext db,
    IEnumerable<IServiceProvisioner> provisioners,
    IBindingCredentialsCodec codec,
    IEnvVarWriter envVarWriter,
    IBindingEnvVarMapper envVarMapper,
    IClock clock)
    : ICommandHandler<RotateCredentialsCommand>
{
    public async Task<Result> Handle(RotateCredentialsCommand request, CancellationToken cancellationToken)
    {
        // EF Core 10 no traduce `Id.ToString() == arg` con ValueConverter activo.
        var bindingsList = await db.ServiceBindings.ToListAsync(cancellationToken);
        var binding = bindingsList.FirstOrDefault(b => b.Id.ToString() == request.BindingId);
        if (binding is null)
        {
            return Error.NotFound("binding.not_found", $"Binding '{request.BindingId}' no existe.");
        }
        if (binding.RevokedAt is not null)
        {
            return Error.Conflict("binding.revoked", "No se pueden rotar credenciales de un binding revocado.");
        }
        var svc = await db.ManagedServices.FirstOrDefaultAsync(s => s.Id == binding.ServiceId, cancellationToken);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", "Servicio asociado al binding no existe.");
        }

        var oldCreds = codec.Decode(binding.CredentialsCipher);
        var newCreds = new BindingCredentials(oldCreds.Username, CredentialsGenerator.GeneratePassword(32));

        var provisioner = provisioners.FirstOrDefault(p => p.SupportedType == svc.Type);
        if (provisioner is not null)
        {
            var outcome = await provisioner.RotateAsync(svc, binding, newCreds, cancellationToken);
            if (!outcome.Success)
            {
                return Error.Conflict(outcome.ErrorCode ?? "rotate_failed",
                    outcome.ErrorMessage ?? "Rotación falló");
            }
        }
        binding.RotateCredentials(codec.Encode(newCreds), clock.UtcNow);

        // Reinyectar env vars (sobrescribe value de las que tenían password).
        var envVars = envVarMapper.Build(svc, binding, newCreds);
        await envVarWriter.UpsertManyAsync(
            EnvVarScope.Instance,
            binding.InstanceId,
            source: $"binding:{binding.Id}",
            vars: envVars,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
