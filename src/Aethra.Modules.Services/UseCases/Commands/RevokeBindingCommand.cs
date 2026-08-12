using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Binding;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.UseCases.Commands;

public sealed record RevokeBindingCommand(string BindingId) : ICommand;

internal sealed class RevokeBindingHandler(
    ServicesDbContext db,
    IEnumerable<IServiceProvisioner> provisioners,
    IBindingCredentialsCodec bindingCodec,
    IEnvVarWriter envVarWriter,
    IClock clock,
    ILogger<RevokeBindingHandler> logger)
    : ICommandHandler<RevokeBindingCommand>
{
    public async Task<Result> Handle(RevokeBindingCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.BindingId, out var parsed) || parsed.Value.Prefix != "bnd")
        {
            return Error.NotFound("binding.not_found", $"Binding '{request.BindingId}' no existe.");
        }
        var typedId = new ServiceBindingId(parsed.Value);

        var binding = await db.ServiceBindings.FirstOrDefaultAsync(b => b.Id == typedId, cancellationToken);
        if (binding is null)
        {
            return Error.NotFound("binding.not_found", $"Binding '{request.BindingId}' no existe.");
        }
        if (binding.RevokedAt is not null)
        {
            return Result.Success();
        }

        var svc = await db.ManagedServices.FirstOrDefaultAsync(s => s.Id == binding.ServiceId, cancellationToken);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", "Servicio asociado al binding no existe.");
        }

        var provisioner = provisioners.FirstOrDefault(p => p.SupportedType == svc.Type);
        if (provisioner is not null && binding.ProvisionedAt is not null)
        {
            BindingCredentials credentials;
            try
            {
                credentials = bindingCodec.Decode(binding.CredentialsCipher);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "No se pudieron descifrar credenciales del binding {Id}; revoke abortado", binding.Id);
                return Error.Failure("binding.credentials_unreadable",
                    "No se pudieron leer las credenciales originales del binding; no se puede confirmar la revocacion.");
            }

            var outcome = await provisioner.RevokeAsync(svc, binding, credentials, cancellationToken);
            if (!outcome.Success)
            {
                logger.LogWarning("Revoke del provisioner fallo para binding {Id}: {Code} {Msg}",
                    binding.Id, outcome.ErrorCode, outcome.ErrorMessage);
                return Error.Conflict(outcome.ErrorCode ?? "revoke_failed",
                    outcome.ErrorMessage ?? "Revocacion fallo");
            }
        }

        binding.Revoke(clock.UtcNow);
        await envVarWriter.RemoveBySourceAsync(
            EnvVarScope.Instance,
            binding.InstanceId,
            $"binding:{binding.Id}",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
