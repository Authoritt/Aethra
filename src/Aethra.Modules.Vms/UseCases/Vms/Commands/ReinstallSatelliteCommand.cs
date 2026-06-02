using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Modules.Vms.Infrastructure.Provisioning;
using Aethra.Modules.Vms.Infrastructure.Security;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aethra.Modules.Vms.UseCases.Vms.Commands;

/// <summary>
/// Re-instala el satélite usando las credenciales SSH ya cifradas en <c>Vm.SshCredentialsCipher</c>.
/// Falla si no hay credenciales guardadas (el operador debe usar <see cref="AutoInstallSatelliteCommand"/>
/// con credenciales primero).
/// </summary>
public sealed record ReinstallSatelliteCommand(
    string VmId,
    bool InstallContainerRuntime = false,
    string ContainerRuntime = "docker") : ICommand<AutoInstallSatelliteResult>;

internal sealed class ReinstallSatelliteHandler(
    VmsDbContext db,
    IClock clock,
    ISshCredentialsCodec codec,
    IInstallationJobQueue queue,
    IConfiguration configuration) : ICommandHandler<ReinstallSatelliteCommand, AutoInstallSatelliteResult>
{
    public async Task<Result<AutoInstallSatelliteResult>> Handle(ReinstallSatelliteCommand request,
        CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.VmId, out var parsed) || parsed.Value.Prefix != "vm")
        {
            return Error.Validation("vm.invalid_id", "ID de VM inválido.");
        }
        var typedId = new VmId(parsed.Value);
        var vm = await db.Vms.FirstOrDefaultAsync(v => v.Id == typedId, cancellationToken);
        if (vm is null)
        {
            return Error.NotFound("vm.not_found", $"No existe la VM '{request.VmId}'.");
        }
        if (vm.SshCredentialsCipher is null)
        {
            return Error.Validation("vm.no_saved_ssh_credentials",
                "No hay credenciales SSH guardadas. Usa el endpoint de auto-install primero.");
        }

        var creds = codec.Decode(vm.SshCredentialsCipher);
        var token = vm.RotateToken(clock.UtcNow);
        var centralUrl = ResolveCentralUrl(configuration);
        var options = new InstallOptions(
            CentralUrl: centralUrl,
            TokenPlaintext: token,
            ContainerRuntime: request.ContainerRuntime,
            InstallContainerRuntime: request.InstallContainerRuntime);

        vm.BeginInstall(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(new InstallationJob(request.VmId, options, creds), cancellationToken);

        return new AutoInstallSatelliteResult(
            VmId: request.VmId,
            Status: "Installing",
            InstallUrl: $"/api/vms/{request.VmId}/install/status",
            StreamHub: "/hubs/dashboard");
    }

    private static string ResolveCentralUrl(IConfiguration configuration)
    {
        var explicitUrl = configuration["Aethra:CentralPublicUrl"];
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return explicitUrl.TrimEnd('/');
        }
        return "http://localhost:5000";
    }
}
