using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Modules.Vms.Infrastructure.Provisioning;
using Aethra.Modules.Vms.Infrastructure.Security;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aethra.Modules.Vms.UseCases.Vms.Commands;

/// <summary>
/// Input SSH para auto-install. Vive aquí en lugar de en Domain para que el frontend pueda
/// mappearlo 1:1 sin tocar el dominio. <see cref="AuthMethod"/> = "key" | "password";
/// <see cref="Value"/> es el contenido PEM o la contraseña.
/// </summary>
public sealed record SshCredentialsInput(
    string Host,
    int Port,
    string User,
    string AuthMethod,
    string Value);

/// <summary>
/// Auto-instala el satélite en la VM identificada por <see cref="VmId"/>:
/// <list type="bullet">
/// <item>Si <see cref="Credentials"/> no es null, cifra y persiste para futuros reinstalls.</item>
/// <item>Marca <c>InstallStatus = Installing</c> y encola el job en <see cref="IInstallationJobQueue"/>.</item>
/// <item>Si <see cref="DryRun"/> es true, no encola — solo devuelve el plan.</item>
/// </list>
/// </summary>
public sealed record AutoInstallSatelliteCommand(
    string VmId,
    SshCredentialsInput? Credentials,
    bool InstallContainerRuntime,
    string ContainerRuntime,
    bool DryRun = false) : ICommand<AutoInstallSatelliteResult>;

public sealed record AutoInstallSatelliteResult(
    string VmId,
    string Status,
    string InstallUrl,
    string StreamHub,
    string? Plan = null,
    string? Script = null);

public sealed class AutoInstallSatelliteValidator : AbstractValidator<AutoInstallSatelliteCommand>
{
    public AutoInstallSatelliteValidator()
    {
        RuleFor(c => c.VmId).NotEmpty();
        RuleFor(c => c.ContainerRuntime)
            .Must(r => r is "docker" or "podman")
            .WithMessage("ContainerRuntime debe ser 'docker' o 'podman'.");
        When(c => c.Credentials is not null && !c.DryRun, () =>
        {
            RuleFor(c => c.Credentials!.Host).NotEmpty();
            RuleFor(c => c.Credentials!.Port).InclusiveBetween(1, 65535);
            RuleFor(c => c.Credentials!.User).NotEmpty();
            RuleFor(c => c.Credentials!.AuthMethod)
                .Must(m => m is "key" or "password")
                .WithMessage("AuthMethod debe ser 'key' o 'password'.");
            RuleFor(c => c.Credentials!.Value)
                .NotEmpty()
                .MaximumLength(SshCredentials.MaxValueLength);
        });
    }
}

internal sealed class AutoInstallSatelliteHandler(
    VmsDbContext db,
    IClock clock,
    ISshCredentialsCodec codec,
    IInstallationJobQueue queue,
    IConfiguration configuration) : ICommandHandler<AutoInstallSatelliteCommand, AutoInstallSatelliteResult>
{
    public async Task<Result<AutoInstallSatelliteResult>> Handle(AutoInstallSatelliteCommand request,
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

        // Resolver credenciales: del request, o las persistidas previamente.
        SshCredentials creds;
        if (request.Credentials is not null)
        {
            var authMethod = request.Credentials.AuthMethod.Equals("key", StringComparison.OrdinalIgnoreCase)
                ? SshAuthMethod.Key
                : SshAuthMethod.Password;
            creds = new SshCredentials(
                Host: request.Credentials.Host,
                Port: request.Credentials.Port,
                User: request.Credentials.User,
                AuthMethod: authMethod,
                Value: request.Credentials.Value);
            var validation = creds.Validate();
            if (validation is not null)
            {
                return Error.Validation(validation, $"Credenciales SSH inválidas: {validation}");
            }
        }
        else if (vm.SshCredentialsCipher is not null)
        {
            creds = codec.Decode(vm.SshCredentialsCipher);
        }
        else
        {
            return Error.Validation("vm.ssh_credentials_required",
                "Sin credenciales guardadas. Provea credenciales SSH en el body.");
        }

        var token = vm.RotateToken(clock.UtcNow);
        var centralUrl = ResolveCentralUrl(configuration);
        var options = new InstallOptions(
            CentralUrl: centralUrl,
            TokenPlaintext: token,
            ContainerRuntime: request.ContainerRuntime,
            InstallContainerRuntime: request.InstallContainerRuntime);

        if (request.DryRun)
        {
            // En dry_run revertimos la rotación: no queremos invalidar el token actual.
            db.Entry(vm).State = EntityState.Unchanged;
            var script = ScriptBuilder.Build(options);
            var plan = ScriptBuilder.BuildPlan(creds, options);
            return new AutoInstallSatelliteResult(
                VmId: request.VmId,
                Status: "Planned",
                InstallUrl: $"/api/vms/{request.VmId}/install/status",
                StreamHub: $"/hubs/dashboard",
                Plan: plan,
                Script: script);
        }

        // Persistir credenciales cifradas (si vienen en el request — si no, ya estaban guardadas).
        if (request.Credentials is not null)
        {
            vm.SetSshCredentials(codec.Encode(creds), clock.UtcNow);
        }
        vm.BeginInstall(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        await queue.EnqueueAsync(new InstallationJob(request.VmId, options, creds), cancellationToken);

        return new AutoInstallSatelliteResult(
            VmId: request.VmId,
            Status: "Installing",
            InstallUrl: $"/api/vms/{request.VmId}/install/status",
            StreamHub: $"/hubs/dashboard");
    }

    private static string ResolveCentralUrl(IConfiguration configuration)
    {
        var explicitUrl = configuration["Aethra:CentralPublicUrl"];
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return explicitUrl.TrimEnd('/');
        }
        // Fallback razonable para desarrollo.
        return "http://localhost:5000";
    }
}

/// <summary>
/// Comparte la generación del script bash entre el command (dry_run + script endpoint) y
/// el provisioner SSH (que ejecuta esencialmente lo mismo via SSH.NET). Se mantiene como
/// single-source-of-truth para que el bash one-liner y el flow remoto no diverjan.
/// </summary>
internal static class ScriptBuilder
{
    public static string Build(InstallOptions opts)
    {
        // El bash one-liner que el usuario copia/pega. Confía en /install-satellite.sh
        // expuesto público por el central (sirve scripts/install-satellite.sh raw).
        var installFlag = opts.InstallContainerRuntime ? " --install-runtime" : "";
        return $"curl -fsSL {opts.CentralUrl}/install-satellite.sh | sudo bash -s -- " +
               $"--central-url {opts.CentralUrl} " +
               $"--token {opts.TokenPlaintext} " +
               $"--runtime {opts.ContainerRuntime}" +
               installFlag;
    }

    public static string BuildPlan(SshCredentials creds, InstallOptions opts)
    {
        return string.Join('\n',
            $"1. Conectar SSH a {creds.User}@{creds.Host}:{creds.Port} (auth={creds.AuthMethod})",
            "2. Detectar arquitectura (uname -m)",
            $"3. Detectar / instalar {opts.ContainerRuntime}" + (opts.InstallContainerRuntime ? " (con install)" : " (sin install)"),
            $"4. Descargar binario desde {opts.CentralUrl}/api/satellite/binary",
            "5. Extraer en /opt/aethra-satellite",
            "6. Escribir /etc/systemd/system/aethra-satellite.service",
            "7. systemctl daemon-reload + enable --now aethra-satellite",
            "8. Verificar handshake del satélite (timeout 60s)");
    }
}
