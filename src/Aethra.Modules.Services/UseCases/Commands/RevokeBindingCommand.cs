using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.UseCases.Commands;

public sealed record RevokeBindingCommand(string BindingId) : ICommand;

internal sealed class RevokeBindingHandler(
    ServicesDbContext db,
    IEnumerable<IServiceProvisioner> provisioners,
    IEnvVarWriter envVarWriter,
    IClock clock,
    ILogger<RevokeBindingHandler> logger)
    : ICommandHandler<RevokeBindingCommand>
{
    public async Task<Result> Handle(RevokeBindingCommand request, CancellationToken cancellationToken)
    {
        var binding = await db.ServiceBindings
            .FirstOrDefaultAsync(b => b.Id.ToString() == request.BindingId, cancellationToken);
        if (binding is null)
        {
            return Error.NotFound("binding.not_found", $"Binding '{request.BindingId}' no existe.");
        }
        if (binding.RevokedAt is not null)
        {
            return Result.Success();
        }
        var svc = await db.ManagedServices.FirstOrDefaultAsync(
            s => s.Id == binding.ServiceId, cancellationToken);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", "Servicio asociado al binding no existe.");
        }

        // Intentamos revocar credenciales reales en el servicio (best-effort: si el servicio
        // está caído, igualmente marcamos revocado en BD para no bloquear al usuario).
        var provisioner = provisioners.FirstOrDefault(p => p.SupportedType == svc.Type);
        if (provisioner is not null && binding.ProvisionedAt is not null)
        {
            var outcome = await provisioner.RevokeAsync(svc, binding, cancellationToken);
            if (!outcome.Success)
            {
                logger.LogWarning("Revoke del provisioner falló para binding {Id}: {Code} {Msg} (marcamos revocado igual)",
                    binding.Id, outcome.ErrorCode, outcome.ErrorMessage);
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
